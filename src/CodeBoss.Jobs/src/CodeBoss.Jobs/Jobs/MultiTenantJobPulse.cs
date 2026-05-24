using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using CodeBoss.Extensions;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Model;
using CodeBoss.MultiTenant;
using Codeboss.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Quartz.Impl.Matchers;

namespace CodeBoss.Jobs.Jobs;

[DisallowConcurrentExecution]
public class MultiTenantJobPulse(
    IServiceJobRepository repository,
    IServiceJobService service,
    ISimpleTenantsProvider tenantsProvider,
    IOptions<CodeBossJobsOptions> options,
    ILogger<MultiTenantJobPulse> logger) : CodeBossJob(repository, logger)
{
    private readonly CodeBossJobsOptions _options = options.Value;
    private readonly SemaphoreSlim _dbSemaphore = new(options.Value.ConcurrentDbOperations, options.Value.ConcurrentDbOperations); // Limit concurrent DB operations;
    private readonly SemaphoreSlim _schedulerSemaphore= new(options.Value.ConcurrentSchedulerOperations, options.Value.ConcurrentSchedulerOperations); // Limit concurrent scheduler operations;
    
    public override async Task Execute(CancellationToken ct = default)
    {
        Logger.LogInformation("Starting tenant job pulse scheduling cycle");
        try
        {
            await SynchronizeJobsWithDataflowAsync(ct);
            Logger.LogInformation("Tenant job scheduling cycle completed successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during tenant job pulse scheduling cycle");
            throw new JobExecutionException(ex);
        }
    }


    /// <summary>
    /// Enhanced synchronization using Dataflow for parallel processing of tenant jobs
    /// </summary>
    private async Task SynchronizeJobsWithDataflowAsync(CancellationToken ct)
    {
        var metrics = new SyncMetrics();
        var tenantResults = new List<TenantSyncResult>();

        // Step 1: Transform tenants to synchronization batches
        var tenantToSyncBatchBlock = new TransformBlock<ITenant, TenantSyncBatch>(
            async tenant =>
            {
                await _dbSemaphore.WaitAsync(ct);
                try
                {
                    return await BuildSyncBatchForTenantAsync(tenant.Id, ct);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error building sync batch for tenant {TenantId}", tenant.Id);
                    return new TenantSyncBatch { TenantId = tenant.Id, Error = ex.Message };
                }
                finally
                {
                    _dbSemaphore.Release();
                }
            },
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 20,
                MaxDegreeOfParallelism = _options.ConcurrentDbOperations, // Process 5 tenants concurrently for DB operations
                CancellationToken = ct
            });

        // Step 2: Transform sync batches to individual operations
        var batchToOperationBlock = new TransformManyBlock<TenantSyncBatch, JobOperation>(
            syncBatch =>
            {
                if (!string.IsNullOrEmpty(syncBatch.Error)) return [];

                var operations = new List<JobOperation>();
                
                // Add delete operations
                operations.AddRange(syncBatch.JobsToDelete.Select(jobKey => 
                    new JobOperation 
                    { 
                        TenantId = syncBatch.TenantId, 
                        Type = OperationType.Delete, 
                        JobKey = jobKey 
                    }));

                // Add schedule operations
                operations.AddRange(syncBatch.JobsToSchedule.Select(job => 
                    new JobOperation 
                    { 
                        TenantId = syncBatch.TenantId, 
                        Type = OperationType.Schedule, 
                        ServiceJob = job 
                    }));

                // Add reschedule operations
                operations.AddRange(syncBatch.JobsToReschedule.Select(item =>
                    new JobOperation
                    {
                        TenantId = syncBatch.TenantId,
                        Type = OperationType.Reschedule,
                        ServiceJob = item.Job,
                        JobKey = item.JobKey,
                        CronTriggerKey = item.CronTriggerKey,
                        JobTypeChanged = item.JobTypeChanged
                    }));

                return operations;
            },
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 100,
                MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded,
                CancellationToken = ct
            });

        // Step 3: Execute scheduler operations
        var operationExecutionBlock = new TransformBlock<JobOperation, OperationResult<JobOperation>>(
            async operation =>
            {
                await _schedulerSemaphore.WaitAsync(ct);
                try
                {
                    return await ExecuteOperationAsync(operation, ct);
                }
                finally
                {
                    _schedulerSemaphore.Release();
                }
            },
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 50,
                MaxDegreeOfParallelism = _options.ConcurrentSchedulerOperations, // Process 10 scheduler operations concurrently
                CancellationToken = ct
            });

        // Step 4: Collect results and update status
        var resultCollectionBlock = new ActionBlock<OperationResult<JobOperation>>(
            async opResult =>
            {
                // Update metrics
                lock (metrics)
                {
                    switch (opResult.Result.Type)
                    {
                        case OperationType.Delete when opResult.IsSuccess:
                            metrics.TotalJobsDeleted++;
                            break;
                        case OperationType.Schedule when opResult.IsSuccess:
                        case OperationType.Reschedule when opResult.IsSuccess:
                            metrics.TotalJobsScheduleUpdated++;
                            break;
                    }

                    var tenantResult = tenantResults.FirstOrDefault(r => r.TenantId == opResult.Result.TenantId);
                    if (tenantResult == null)
                    {
                        tenantResult = new TenantSyncResult { TenantId = opResult.Result.TenantId };
                        tenantResults.Add(tenantResult);
                    }

                    if (opResult.IsSuccess)
                    {
                        switch (opResult.Result.Type)
                        {
                            case OperationType.Delete:
                                tenantResult.Deleted++;
                                break;
                            case OperationType.Schedule:
                            case OperationType.Reschedule:
                                tenantResult.Updated++;
                                break;
                        }
                    }
                }

                // Handle errors by updating job status in database
                if (!opResult.IsSuccess && opResult.Result.ServiceJob != null)
                {
                    await _dbSemaphore.WaitAsync(ct);
                    try
                    {
                        var errorMessage = opResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";
                        await HandleAndLogError(opResult.Result.ServiceJob, errorMessage, 
                            new Exception(errorMessage), opResult.Result.TenantId, ct);
                    }
                    finally
                    {
                        _dbSemaphore.Release();
                    }
                }

                // Clear error status for successful operations
                if (opResult.IsSuccess && opResult.Result.ServiceJob != null && 
                    (opResult.Result.ServiceJob.LastStatus?.Contains("Error") == true))
                {
                    await _dbSemaphore.WaitAsync(ct);
                    try
                    {
                        await Repository.ClearStatusesAsync(opResult.Result.ServiceJob, opResult.Result.TenantId, ct);
                    }
                    finally
                    {
                        _dbSemaphore.Release();
                    }
                }
            },
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 200,
                MaxDegreeOfParallelism = _options.ConcurrentDbUpdateOperations, // Limit DB update concurrency
                CancellationToken = ct
            });

        // Link the pipeline
        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        
        tenantToSyncBatchBlock.LinkTo(batchToOperationBlock, linkOptions);
        batchToOperationBlock.LinkTo(operationExecutionBlock, linkOptions);
        operationExecutionBlock.LinkTo(resultCollectionBlock, linkOptions);

        // Start processing
        var tenants = tenantsProvider.Tenants().ToList();
        Logger.LogInformation("Processing {TenantCount} tenants", tenants.Count);

        foreach (var tenant in tenants)
        {
            await tenantToSyncBatchBlock.SendAsync(tenant, ct);
        }

        // Complete pipeline and wait
        tenantToSyncBatchBlock.Complete();
        await resultCollectionBlock.Completion;

        // Build and log results
        BuildAndLogResult(metrics.TotalJobsDeleted, metrics.TotalJobsScheduleUpdated, tenantResults);
    }

    private async Task<TenantSyncBatch> BuildSyncBatchForTenantAsync(int tenantId, CancellationToken ct)
    {
        var syncBatch = new TenantSyncBatch { TenantId = tenantId };
        var scheduler = Scheduler;

        try
        {
            // Get active jobs for this tenant
            var activeJobs = (await Repository.GetActiveJobsAsync(tenantId, ct)).ToList();
            Logger.LogInformation("Getting active jobs for tenant: [{0}], Jobs: [{1}]", tenantId, activeJobs.Count);

            // Get scheduled jobs for this tenant
            var groupName = $"tenant_{tenantId}";
            var scheduledQuartzJobs = (await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(groupName), ct))
                .Where(jobKey => jobKey.Group != "System").ToList();

            // Identify jobs to delete
            syncBatch.JobsToDelete = scheduledQuartzJobs.Where(jobKey => 
                !activeJobs.Any(j => service.GetJobKey(j, tenantId).Equals(jobKey))).ToList();

            // Identify new jobs to schedule
            syncBatch.JobsToSchedule = activeJobs.Where(a => 
                !scheduledQuartzJobs.Any(q => q.Equals(service.GetJobKey(a, tenantId)))).ToList();

            // Identify jobs that need rescheduling
            await IdentifyJobsToRescheduleAsync(syncBatch, activeJobs, scheduledQuartzJobs, tenantId, ct);
        }
        catch (Exception ex)
        {
            syncBatch.Error = ex.Message;
            Logger.LogError(ex, "Error building sync batch for tenant {TenantId}", tenantId);
        }

        return syncBatch;
    }

    private async Task IdentifyJobsToRescheduleAsync(TenantSyncBatch syncBatch, List<ServiceJob> activeJobs, 
        List<JobKey> scheduledQuartzJobs, int tenantId, CancellationToken ct)
    {
        var scheduler = Scheduler;
        
        foreach (var jobKey in scheduledQuartzJobs)
        {
            var triggersOfJob = await scheduler.GetTriggersOfJob(jobKey, ct);
            var jobCronTrigger = triggersOfJob.OfType<ICronTrigger>().FirstOrDefault();
            var activeJob = activeJobs.FirstOrDefault(a => service.GetJobKey(a, tenantId).Equals(jobKey));
            
            if (jobCronTrigger == null || activeJob == null) continue;

            bool rescheduleJob = false;
            bool jobTypeChanged = false;

            // Check if cron expression changed
            if (activeJob.CronExpression != jobCronTrigger.CronExpressionString)
                rescheduleJob = true;
            else
            {
                // Check if job type changed
                IJobDetail scheduledJobDetail = await scheduler.GetJobDetail(jobKey, ct);
                var activeJobType = activeJob.GetCompiledType();

                if (scheduledJobDetail != null && activeJobType != null)
                {
                    if (scheduledJobDetail.JobType != activeJobType)
                    {
                        rescheduleJob = true;
                        jobTypeChanged = true;
                    }
                }
            }

            if (rescheduleJob)
            {
                syncBatch.JobsToReschedule.Add(new RescheduleItem
                {
                    Job = activeJob,
                    JobKey = jobKey,
                    CronTriggerKey = jobCronTrigger.Key,
                    JobTypeChanged = jobTypeChanged
                });
            }
        }
    }

    private async Task<OperationResult<JobOperation>> ExecuteOperationAsync(JobOperation operation, CancellationToken ct)
    {
        try
        {
            var scheduler = Scheduler;

            switch (operation.Type)
            {
                case OperationType.Delete:
                    await scheduler.DeleteJob(operation.JobKey, ct);
                    return OperationResult<JobOperation>.Success(operation);

                case OperationType.Schedule:
                    try
                    {
                        IJobDetail jobDetail = service.BuildQuartzJob(operation.ServiceJob, operation.TenantId);
                        if (jobDetail == null)
                        {
                            return OperationResult<JobOperation>.Fail("Failed to build job detail");
                        }

                        ITrigger jobTrigger = service.BuildJobTrigger(operation.ServiceJob, operation.TenantId);
                        
                        if (operation.ServiceJob.CronExpression != ServiceJob.NeverScheduledCronExpression)
                        {
                            await scheduler.ScheduleJob(jobDetail, jobTrigger, ct);
                        }
                        return OperationResult<JobOperation>.Success(operation);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error executing operation: {OperationType} for tenant: {TenantId}", operation.Type, operation.TenantId);
                        return OperationResult<JobOperation>.Fail(ex.Message);
                    }

                case OperationType.Reschedule:
                    try
                    {
                        ITrigger newJobTrigger = service.BuildJobTrigger(operation.ServiceJob, operation.TenantId);

                        if (operation.JobTypeChanged)
                        {
                            // job class changed: replace job detail + trigger
                            IJobDetail newJobDetail = service.BuildQuartzJob(operation.ServiceJob, operation.TenantId);
                            await scheduler.DeleteJob(operation.JobKey, ct);
                            if (newJobDetail != null)
                            {
                                await scheduler.ScheduleJob(newJobDetail, newJobTrigger, ct);
                            }
                        }
                        else if (operation.CronTriggerKey != null)
                        {
                            // cron-only change: atomic trigger swap
                            await scheduler.RescheduleJob(operation.CronTriggerKey, newJobTrigger, ct);
                        }
                        else
                        {
                            // fallback: no captured trigger key, schedule fresh
                            IJobDetail jobDetail = service.BuildQuartzJob(operation.ServiceJob, operation.TenantId);
                            await scheduler.DeleteJob(operation.JobKey, ct);
                            if (jobDetail != null)
                            {
                                await scheduler.ScheduleJob(jobDetail, newJobTrigger, ct);
                            }
                        }
                        return OperationResult<JobOperation>.Success(operation);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error executing operation: {OperationType} for tenant: {TenantId}", operation.Type, operation.TenantId);
                        return OperationResult<JobOperation>.Fail(ex.Message);
                    }

                default:
                    return OperationResult<JobOperation>.Fail("Unknown operation type");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing operation: {OperationType} for tenant: {TenantId}", operation.Type, operation.TenantId);
            return OperationResult<JobOperation>.Fail(ex.Message);
        }
    }

    private void BuildAndLogResult(int totalJobsDeleted, int totalJobsScheduleUpdated, List<TenantSyncResult> tenantResults)
    {
        Result = string.Empty;
        if (totalJobsDeleted > 0)
        {
            Result += $"Deleted {totalJobsDeleted} job schedule(s)";
        }

        if (totalJobsScheduleUpdated > 0)
        {
            Result += (Result.IsNullOrEmpty() ? "" : " and ") + $"Updated {totalJobsScheduleUpdated} schedule(s)";
        }

        var results = tenantResults
            .Where(r => r.Deleted > 0 || r.Updated > 0)
            .Select(r => $"Tenant {r.TenantId}: {r.Deleted} deleted, {r.Updated} updated")
            .ToList();
        
        if (results.Any())
        {
            Result += "\n" + string.Join("\n", results);
        }
        
        Logger.LogInformation(Result);
    }

    private async Task HandleAndLogError(ServiceJob job, string errorStatus, Exception ex, int? tenantId, CancellationToken ct)
    {
        Logger.LogError(ex, "Error scheduling job {JobName} for tenant {TenantId}", job.Name, tenantId);
        string message = $"Error scheduling the job: {job.Name}.\n\n{ex.Message}";
        await Repository.UpdateStatusMessagesAsync(job.Id, tenantId, message, errorStatus, ct);
    }
}

// Supporting classes for the dataflow pipeline
[DebuggerDisplay("TenantId = {TenantId}, " +
                 "JobsToDelete = {JobsToDelete.Count}, " +
                 "JobsToSchedule = {JobsToSchedule.Count}, " +
                 "JobsToReschedule = {JobsToReschedule.Count}"
                 )]
public class TenantSyncBatch
{
    public int TenantId { get; set; }
    public List<JobKey> JobsToDelete { get; set; } = new();
    public List<ServiceJob> JobsToSchedule { get; set; } = new();
    public List<RescheduleItem> JobsToReschedule { get; set; } = new();
    public string Error { get; set; }
}

[DebuggerDisplay("JobKey = {JobKey.Name}")]
public class RescheduleItem
{
    public ServiceJob Job { get; set; }
    public JobKey JobKey { get; set; }
    public TriggerKey CronTriggerKey { get; set; }
    public bool JobTypeChanged { get; set; }
}

[DebuggerDisplay("TenantId = {TenantId}, ServiceJobId = {ServiceJob.Id}")]
public class JobOperation
{
    public int TenantId { get; set; }
    public OperationType Type { get; set; }
    public ServiceJob ServiceJob { get; set; }
    public JobKey JobKey { get; set; }
    public TriggerKey CronTriggerKey { get; set; }
    public bool JobTypeChanged { get; set; }
}

public enum OperationType
{
    Delete,
    Schedule,
    Reschedule
}

[DebuggerDisplay("TotalDeleted = {TotalJobsDeleted}, TotalUpdated = {TotalJobsScheduleUpdated}")]
public class SyncMetrics
{
    public int TotalJobsDeleted { get; set; }
    public int TotalJobsScheduleUpdated { get; set; }
}

[DebuggerDisplay("TenantId = {TenantId}, Deleted = {Deleted}, Updated = {Updated}")]
public class TenantSyncResult
{
    public int TenantId { get; set; }
    public int Deleted { get; set; }
    public int Updated { get; set; }
}
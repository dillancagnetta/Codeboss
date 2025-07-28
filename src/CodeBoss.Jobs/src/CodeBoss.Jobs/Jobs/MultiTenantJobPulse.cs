using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBoss.Extensions;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Model;
using CodeBoss.MultiTenant;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;

namespace CodeBoss.Jobs.Jobs;

public class MultiTenantJobPulse(
    IServiceJobRepository repository, 
    IServiceJobService service,
    ISimpleTenantsProvider tenantsProvider,
    ILogger<CodeBossJob> logger) : CodeBossJob(repository, logger)
{
    public override async Task Execute(CancellationToken ct = default)
    {
        await SynchronizeJobs(ct);
    }
    
    /// <summary>
    /// Synchronizes the jobs in the database with the scheduled Quartz jobs.
    /// This method performs the following operations:
    /// 1. Deletes jobs that are no longer active in the database.
    /// 2. Schedules new active jobs that are not yet scheduled.
    /// 3. Reschedules existing jobs if their schedule or details have changed.
    /// </summary>
    private async Task SynchronizeJobs(CancellationToken ct)
    {
        int totalJobsDeleted = 0;
        int totalJobsScheduleUpdated = 0;
        var results = new List<string>();

        // Get all tenant IDs
        var tenants = tenantsProvider.Tenants();
        // Process 5 tenants concurrently
        var batches = tenants.Chunk(5).ToList();
        foreach (var batch in batches)
        {
            // Sync jobs for each tenant
            var tasks = batch.Select(tenant => SynchronizeJobsForTenantAsync(tenant.Id, ct));
            var batchResults = await Task.WhenAll(tasks);
            
            // Process results from this batch
            foreach (var (tenant, (deleted, updated)) in batch.Zip(batchResults)) //combine each tenant with its corresponding result tuple
            {
                totalJobsDeleted += deleted;
                totalJobsScheduleUpdated += updated;
                
                if (deleted > 0 || updated > 0)
                {
                    results.Add($"Tenant {tenant.Id}: {deleted} deleted, {updated} updated");
                }
            }
        }

        // Build result
        BuildAndLogResult(totalJobsDeleted, totalJobsScheduleUpdated, results);
    }

    private void BuildAndLogResult(int totalJobsDeleted, int totalJobsScheduleUpdated, List<string> results)
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
        
        if (results.Any())
        {
            Result += "\n" + string.Join("\n", results);
        }
        
        Logger.LogInformation(Result);
    }

    private async Task<(int deleted, int updated)> SynchronizeJobsForTenantAsync(int tenantId, CancellationToken ct)
    {
        int jobsDeleted = 0;
        int jobsScheduleUpdated = 0;

        var scheduler = Scheduler;
        
        // Get active jobs for this tenant
        var activeJobs = (await Repository.GetActiveJobsAsync(tenantId, ct)).ToList();
        Logger.LogInformation("Getting active jobs for tenant: [{0}], Jobs: [{1}]", tenantId, activeJobs.Count);
        
        // Get scheduled jobs for this tenant
        var groupName = $"tenant_{tenantId}";
        var scheduledQuartzJobs = (await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(groupName), ct))
            .Where(jobKey => jobKey.Group != "System").ToList();

        // Delete jobs no longer active
        var quartzJobsToDelete = scheduledQuartzJobs.Where(jobKey => 
            !activeJobs.Any(j => service.GetJobKey(j, tenantId).Equals(jobKey)));
        
        foreach (JobKey jobKey in quartzJobsToDelete)
        {
            await scheduler.DeleteJob(jobKey, ct);
            jobsDeleted++;
        }

        // Add new jobs
        var newActiveJobs = activeJobs.Where(a => 
            !scheduledQuartzJobs.Any(q => q.Equals(service.GetJobKey(a, tenantId))));
        
        foreach (var job in newActiveJobs)
        {
            const string errorSchedulingStatus = "Error scheduling Job";
            try
            {
                IJobDetail jobDetail = service.BuildQuartzJob(job, tenantId);
                if (jobDetail == null) continue;

                ITrigger jobTrigger = service.BuildJobTrigger(job, tenantId);

                if (job.CronExpression != ServiceJob.NeverScheduledCronExpression)
                {
                    await scheduler.ScheduleJob(jobDetail, jobTrigger, ct);
                    jobsScheduleUpdated++;
                }

                if (job.LastStatus == errorSchedulingStatus) 
                    await Repository.ClearStatusesAsync(job, tenantId, ct);
            }
            catch (Exception ex)
            {
                await HandleAndLogError(job, errorSchedulingStatus, ex, tenantId, ct);
            }
        }

        // Handle rescheduling (similar to original logic but with tenant awareness)
        scheduledQuartzJobs = (await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(groupName), ct))
            .Where(jobKey => jobKey.Group != "System").ToList();
            
        foreach (var jobKey in scheduledQuartzJobs)
        {
            var triggersOfJob = await scheduler.GetTriggersOfJob(jobKey, ct);
            var jobCronTrigger = triggersOfJob.OfType<ICronTrigger>().FirstOrDefault();
            var activeJob = activeJobs.FirstOrDefault(a => service.GetJobKey(a, tenantId).Equals(jobKey));
            
            if (jobCronTrigger == null || activeJob == null) continue;

            bool rescheduleJob = false;

            if (activeJob.CronExpression != jobCronTrigger.CronExpressionString) 
                rescheduleJob = true;
            else
            {
                IJobDetail scheduledJobDetail = await scheduler.GetJobDetail(jobKey, ct);
                var activeJobType = activeJob.GetCompiledType();

                if (scheduledJobDetail != null && activeJobType != null)
                {
                    if (scheduledJobDetail.JobType != activeJobType) 
                        rescheduleJob = true;
                }
            }

            if (rescheduleJob)
            {
                const string errorReschedulingStatus = "Error re-scheduling Job";
                try
                {
                    ITrigger newJobTrigger = service.BuildJobTrigger(activeJob, tenantId);
                    await scheduler.DeleteJob(jobKey, ct);
                    await scheduler.RescheduleJob(jobCronTrigger.Key, newJobTrigger, ct);
                    jobsScheduleUpdated++;

                    if (activeJob.LastStatus == errorReschedulingStatus)
                    {
                        await Repository.ClearStatusesAsync(activeJob, tenantId, ct);
                    }
                }
                catch (Exception ex)
                {
                    await HandleAndLogError(activeJob, errorReschedulingStatus, ex, tenantId, ct);
                }
            }
        }

        return (jobsDeleted, jobsScheduleUpdated);
    }

    private async Task HandleAndLogError(ServiceJob job, string errorStatus, Exception ex, int? tenantId, CancellationToken ct)
    {
        Logger.LogError(ex.Message);
        string message = $"Error scheduling the job: {job.Name}.\n\n{ex.Message}";
        await Repository.UpdateStatusMessagesAsync(job.Id, tenantId, message, errorStatus, ct);
    }
}
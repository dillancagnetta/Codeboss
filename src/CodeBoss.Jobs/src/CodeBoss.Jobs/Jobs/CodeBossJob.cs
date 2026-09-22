using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Model;
using Microsoft.Extensions.Logging;
using Quartz;

namespace CodeBoss.Jobs.Jobs
{
    public abstract class CodeBossJob(IServiceJobRepository repository, ILogger logger) : ICodeBossJob
    {
        protected readonly IServiceJobRepository Repository = repository;
        protected readonly ILogger Logger = logger;
        
        /// <summary>
        /// Gets the job identifier.
        /// </summary>
        /// <returns>System.Int32.</returns>
        public int GetJobId()
        {
            return ServiceJobId;
        }

        /// <summary>
        /// Gets the TenantId.
        /// </summary>
        /// <value>The TenantId context.</value>
        public int? TenantId { get; private set; }

        /// <summary>
        /// Gets the service job identifier.
        /// </summary>
        /// <value>The service job identifier.</value>
        public int ServiceJobId { get; private set; }

        /// <summary>
        /// Gets the name of the service job.
        /// </summary>
        /// <value>The name of the service job.</value>
        public string ServiceJobName => ServiceJob?.Name ?? "JobPulse";

        /// <summary>
        /// Gets the service job.
        /// </summary>
        /// <value>The service job.</value>
        protected ServiceJob ServiceJob { get; set; }

        /// <summary>
        /// The job row backing this execution, or null for a System job. Read by
        /// <c>CodeBossJobStatusListener</c> to decide whether a failure should be retried, since the
        /// retry budget lives on the row.
        /// </summary>
        public ServiceJob ServiceJobDefinition => ServiceJob;

        /// <summary>
        /// Parameters for THIS run: the job row's <c>JobParameters</c>, with any one-off values
        /// passed to <c>IJobCommands.RunNowAsync</c> layered over them.
        ///
        /// <para>Read from Quartz's merged map, where trigger data wins over job data. That is what
        /// makes a "run now with these overrides" request visible to the job body without mutating
        /// the stored job definition.</para>
        /// </summary>
        protected IReadOnlyDictionary<string, string> Parameters { get; private set; } =
            new Dictionary<string, string>();

        /// <summary>
        /// Which attempt this execution is: 1 for the scheduled fire, 2 and above for retries
        /// scheduled after a failure.
        ///
        /// <para>Useful for work that should behave differently on a retry — skipping an expensive
        /// step already known to have succeeded, or widening a query window after a gap.</para>
        /// </summary>
        public int Attempt { get; private set; } = 1;

        /// <summary>
        /// Gets the scheduler.
        /// </summary>
        /// <value>The scheduler.</value>
        internal IScheduler Scheduler { get; private set; }
        
        /// <summary>
        /// Gets or sets the result.
        /// </summary>
        /// <value>The result.</value>
        public string Result { get; set; }


        Task Quartz.IJob.Execute( Quartz.IJobExecutionContext context )
        {
            Logger?.LogInformation("Executing quartz job: {0}", ServiceJobName);
            return ExecuteInternal(context);
        }
        
        /// <summary>
        /// Executes this instance.
        /// </summary>
        public abstract Task Execute(CancellationToken ct = default);

        private async Task ExecuteInternal(IJobExecutionContext context)
        {
            await InitializeFromJobContext(context);

            var timeout = ResolveTimeout();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            if (timeout is not null) cts.CancelAfter(timeout.Value);

            var started = DateTime.UtcNow;

            try
            {
                Logger?.LogInformation("Executing job {JobName} (attempt {Attempt})", ServiceJobName, context.GetAttempt());

                await Execute(cts.Token);

                var elapsed = DateTime.UtcNow - started;
                if (timeout is not null && elapsed > timeout.Value)
                {
                    // The job finished, but only because it ignored its cancellation token. Report it
                    // as a success — the work really did complete — and make the overrun visible.
                    Logger?.LogWarning(
                        "Job {JobName} ran for {Elapsed}, over its {Timeout} timeout, because it did not " +
                        "observe its CancellationToken. Pass the token through to make the timeout effective.",
                        ServiceJobName, elapsed, timeout.Value);
                }

                Logger?.LogInformation("Job {JobName} completed in {Elapsed}", ServiceJobName, elapsed);
            }
            catch (OperationCanceledException) when (timeout is not null
                                                     && cts.IsCancellationRequested
                                                     && !context.CancellationToken.IsCancellationRequested)
            {
                // Our deadline fired, not the scheduler's shutdown.
                Logger?.LogWarning("Job {JobName} timed out after {Timeout}", ServiceJobName, timeout.Value);
                throw new JobTimeoutException(ServiceJobName, timeout.Value);
            }
            catch (Exception e)
            {
                Logger?.LogError(e, "Job {JobName} failed", ServiceJobName);
                throw;
            }
        }

        private static IReadOnlyDictionary<string, string> ReadParameters(IJobExecutionContext context)
        {
            var merged = context.MergedJobDataMap;
            var parameters = new Dictionary<string, string>(merged?.Count ?? 0);

            if (merged is null) return parameters;

            foreach (var entry in merged)
            {
                parameters[entry.Key] = entry.Value?.ToString() ?? string.Empty;
            }

            return parameters;
        }

        /// <summary>Configured timeout for this run, or null when unlimited.</summary>
        private TimeSpan? ResolveTimeout() =>
            ServiceJob?.TimeoutSeconds is > 0
                ? TimeSpan.FromSeconds(ServiceJob.TimeoutSeconds.Value)
                : null;

        private async Task InitializeFromJobContext(IJobExecutionContext context)
        {
            var serviceJobId = context.GetJobIdFromQuartz();
            Scheduler = context.Scheduler;
            TenantId = context.GetTenantIdFromQuartz();
            Parameters = ReadParameters(context);
            Attempt = context.GetAttempt();
            
            // Skip JobPulse job
            if (!context.JobDetail.Key.Group.Equals(JobGroups.System))
            {
                ServiceJobId = serviceJobId;
                ServiceJob = await Repository.FindAsync(new JobRef(serviceJobId, TenantId));
                Logger?.LogInformation("Initialized From JobContext: {0} with Id: {1}", ServiceJobName, serviceJobId);
            }
        }
        
        /// <summary>
        /// Updates the last status message.
        /// NOTE: This method has a read and a write database operation and also writes to the Logger with DEBUG level logging.
        /// </summary>
        /// <param name="status">The status message.</param>
        protected Task UpdateLastStatusMessage(string status)
        {
            Result = status;

            return Repository.SetStatusAsync(new JobRef(ServiceJobId, TenantId), JobRunStatus.Running, status);
        }
        
        
        /// <summary>
        /// Updates the last status message.
        /// NOTE: This method has a read and a write database operation and also writes to the Logger with DEBUG level logging.
        /// </summary>
        protected Task UpdateStatusMessagesAsync(string message, string status)
        {
            Result = status;

            return Repository.SetStatusAsync(new JobRef(ServiceJobId, TenantId), JobRunStatus.Running, message);
        }

        /// <summary>
        /// Persists a progress message for this run immediately. Costs a database round trip, so use
        /// it for long jobs reporting milestones, not in a loop.
        /// </summary>
        protected Task ReportProgressAsync(string message, CancellationToken ct = default)
        {
            Result = message;

            return Repository.SetStatusAsync(new JobRef(ServiceJobId, TenantId), JobRunStatus.Running, message, ct);
        }
    }
}
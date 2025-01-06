using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Model;
using Microsoft.Extensions.Logging;
using Quartz;

namespace CodeBoss.Jobs.Jobs
{
    public abstract class CodeBossJob(IServiceJobRepository repository, ILogger<CodeBossJob> logger) : ICodeBossJob
    {
        /// <summary>
        /// Gets the job identifier.
        /// </summary>
        /// <returns>System.Int32.</returns>
        public int GetJobId()
        {
            return ServiceJobId;
        }

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
        /// Gets the scheduler.
        /// </summary>
        /// <value>The scheduler.</value>
        internal IScheduler Scheduler { get; private set; }
        
        /// <summary>
        /// Gets or sets the result.
        /// </summary>
        /// <value>The result.</value>
        public string Result { get; set; }
        
        private readonly IServiceJobRepository _repository = repository;

        Task Quartz.IJob.Execute( Quartz.IJobExecutionContext context )
        {
            logger?.LogInformation("Executing quartz job: {0}", ServiceJobName);
            return ExecuteInternal(context);
        }
        
        /// <summary>
        /// Executes this instance.
        /// </summary>
        public abstract Task Execute(CancellationToken ct = default);

        private async Task ExecuteInternal(IJobExecutionContext context)
        {
            InitializeFromJobContext(context).Wait();

            try
            {
                logger?.LogInformation("Executing job: {0} at [{1}]", ServiceJobName, DateTime.Now);
                await Execute(context.CancellationToken);
                //await repository.SaveChangesAsync(context.CancellationToken);
                logger?.LogInformation("Executing job complete: {0} at [{1}]", ServiceJobName, DateTime.Now);
            }
            catch (Exception e)
            {
                logger?.LogError(e.Message);
                throw;
            }

        }

        private async Task InitializeFromJobContext(IJobExecutionContext context)
        {
            var serviceJobId = context.GetJobIdFromQuartz();
            Scheduler = context.Scheduler;
            // Skip JobPulse job
            if (!context.JobDetail.Key.Group.Equals("System"))
            {
                ServiceJobId = serviceJobId;
                ServiceJob = await _repository.GetByIdAsync( serviceJobId );
                logger?.LogInformation("Initialized From JobContext: {0} with Id: {1}", ServiceJobName, serviceJobId);
            }
        }
        
        /// <summary>
        /// Updates the last status message.
        /// NOTE: This method has a read and a write database operation and also writes to the Rock Logger with DEBUG level logging.
        /// </summary>
        /// <param name="statusMessage">The status message.</param>
        public async Task UpdateLastStatusMessage( string statusMessage )
        {
            Result = statusMessage;

            await _repository.UpdateLastStatusMessageAsync(ServiceJobId, statusMessage);
        }
    }
}
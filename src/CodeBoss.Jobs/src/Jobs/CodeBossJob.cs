using System.Threading.Tasks;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Model;
using Quartz;

namespace CodeBoss.Jobs.Jobs
{
    public abstract class CodeBossJob(IServiceJobRepository repository) : ICodeBossJob
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
        public string ServiceJobName => ServiceJob?.Name;

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

        public virtual Task Execute(IJobExecutionContext context)
        {
            return ExecuteInternal(context);
        }
        
        /// <summary>
        /// Executes this instance.
        /// </summary>
        public abstract Task Execute();

        private async Task ExecuteInternal(IJobExecutionContext context)
        {
            await InitializeFromJobContext( context );

        }

        private async Task InitializeFromJobContext(IJobExecutionContext context)
        {
            var serviceJobId = context.GetJobIdFromQuartz();
            ServiceJobId = serviceJobId;
            ServiceJob = await _repository.GetByIdAsync( serviceJobId );
            Scheduler = context.Scheduler;
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
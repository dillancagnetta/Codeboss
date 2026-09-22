using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Codeboss.Types;
using CronExpressionDescriptor;

namespace CodeBoss.Jobs.Model
{
    public class ServiceJob : IAggregateRoot<int>
    {
        [Key]
        public int Id { get; set; }
        
        /// <summary>
        /// Gets or sets the unique Key of the Job. This property is required.
        /// </summary>
        /// <value>
        [Required]
        public Guid JobKey { get; set; }
        
        /// <summary>
        /// Gets or sets the friendly Name of the Job. This property is required.
        /// </summary>
        /// <value>
        [Required]
        [MaxLength( 100 )]
        public string Name { get; set; }
        
        public string? Description { get; set; }
        
        [MaxLength( 260 )]
        public string? Assembly { get; set; }
        
        /// <summary>
        /// Gets or sets the fully qualified class name with Namespace of the Job class. This property is required.
        /// </summary>
        [Required]
        [MaxLength( 260 )]
        public string Class { get; set; }
        
        /// <summary>
        /// Gets or sets the Cron Expression that is used to schedule the Job. This property is required.
        /// </summary>
        [Required]
        [MaxLength( 120 )]
        public string CronExpression { get; set; }
        
        /// <summary>
        /// Gets or sets a flag indicating if the Job is active.
        /// </summary>
        public bool IsActive { get; set; }
        
        /// <summary>
        /// Gets or sets the date and time that the Job last completed successfully.
        /// </summary>
        public DateTime? LastSuccessfulRunDateTime { get; set; }
        
        /// <summary>
        /// Gets or sets the date and time that the job last ran.
        /// </summary>
        public DateTime? LastRunDateTime { get; set; }
        
        /// <summary>
        /// Gets or set the amount of time, in seconds, that it took the job to run the last time that it ran.
        /// </summary>
        public int? LastRunDurationSeconds { get; set; }
        
        /// <summary>
        /// Gets or sets the completion status that was returned by the Job the last time that it ran.
        /// </summary>
        [MaxLength( 50 )]
        public string? LastStatus { get; set; }
        
        /// <summary>
        /// Gets or sets the status message that was returned the last time that the job was run. In most cases this will be used
        /// in the event of an exception to return the exception message.
        /// </summary>
        public string? LastStatusMessage { get; set; }

        /// <summary>
        /// <see cref="LastStatus"/> parsed back into <see cref="JobRunStatus"/>, or
        /// <see cref="JobRunStatus.None"/> for an unrecognised or legacy free-text value.
        /// </summary>
        public JobRunStatus LastRunStatus =>
            System.Enum.TryParse<JobRunStatus>(LastStatus, ignoreCase: true, out var parsed)
                ? parsed
                : JobRunStatus.None;
        
        
        public Dictionary<string, string>? JobParameters { get; set; }

        /// <summary>
        /// Hard limit on a single execution, in seconds. Null (the default) means no timeout.
        ///
        /// <para>Enforced COOPERATIVELY: the job's <see cref="System.Threading.CancellationToken"/> is
        /// cancelled at the deadline. A job body that ignores its token runs to completion regardless —
        /// there is no safe way to abort a thread mid-work — and the overrun is logged.</para>
        /// </summary>
        public int? TimeoutSeconds { get; set; }

        /// <summary>
        /// Retries after a failed or timed-out run, before giving up until the next scheduled fire.
        /// 0 (the default) preserves the historical behaviour of no retries.
        /// </summary>
        public int MaxRetries { get; set; } = 0;

        /// <summary>Base delay for the exponential backoff between retries. Default 30 seconds.</summary>
        public int RetryBackoffBaseSeconds { get; set; } = 30;

        /// <summary>
        /// Ceiling on any single backoff delay. Default 15 minutes. Without a cap, a handful of
        /// retries reaches multi-hour delays.
        /// </summary>
        public int RetryBackoffMaxSeconds { get; set; } = 900;

        /// <summary>
        /// Per-job misfire override. Null (the default) uses the global
        /// <c>CodeBossJobsOptions.MisfirePolicy</c>.
        /// </summary>
        public MisfirePolicy? MisfirePolicy { get; set; }

        /// <summary>
        /// Prevents overlapping executions of this job. Default true.
        ///
        /// <para>A per-row equivalent of <c>[DisallowConcurrentExecution]</c>, which is a class-level
        /// attribute and therefore cannot vary between two rows sharing a job class. Against a
        /// clustered store Quartz enforces this across every node, not just within one process.</para>
        /// </summary>
        public bool DisallowConcurrentExecution { get; set; } = true;
        
        /// <summary>
        /// Gets or sets a comma delimited list of email address that should receive notification emails for this job. Notification
        /// emails are sent to these email addresses based on the completion status of the Job and the <see cref="JobNotificationStatus"/>
        /// property of this job.
        /// </summary>
        [MaxLength( 1000 )]
        public string? NotificationEmails { get; set; }
        
        /// <summary>
        /// Gets or sets the NotificationStatus for this job, this property determines when notification emails should be sent to the <see cref="NotificationEmails"/>
        /// that are associated with this Job
        /// </summary>
        /// <value>
        /// An <see cref="JobNotificationStatus"/> that indicates when notification emails should be sent for this job.
        /// When this value is <c>JobNotificationStatus.All</c> a notification email will be sent when the Job completes with any completion status.
        /// When this value is <c>JobNotificationStatus.Success</c> a notification email will be sent when the Job has completed successfully.
        /// When this value is <c>JobNotificationStatus.Error</c> a notification email will be sent when the Job completes with an error status.
        /// When this value is <c>JobNotificationStatus.None</c> notifications will not be sent when the Job completes with any status.
        /// </value>
        public JobNotificationStatus NotificationStatus { get; set; } = JobNotificationStatus.None;
        
        /// <summary>
        /// Gets or sets a value indicating whether jobs should be logged in ServiceJobHistory
        /// </summary>
        /// <value>
        ///   <c>true</c> if [enable history]; otherwise, <c>false</c>.
        /// </value>
        public bool EnableHistory { get; set; } = false;
        
        public int HistoryCount { get; set; } = 500;
        
        /// <summary>
        /// Gets or sets the a list of previous values that this attribute value had (If ServiceJob.EnableHistory is enabled)
        /// </summary>
        /// <value>
        /// The history of service jobs.
        /// </value>
        public virtual ICollection<ServiceJobHistory> ServiceJobHistory { get; set; } = new Collection<ServiceJobHistory>();
        
        /// <summary>
        /// Gets the cron description.
        /// </summary>
        /// <value>
        /// The cron description.
        /// </value>
        public virtual string CronDescription => ExpressionDescriptor.GetDescription( this.CronExpression, new Options { ThrowExceptionOnParseError = false } );

        /// <summary>
        /// The never scheduled cron expression. This will only fire the job in the year 2099. This is useful for jobs
        /// that should be run only on demand, such as rebuilding Streak data.
        /// </summary>
        public static readonly string NeverScheduledCronExpression = "0 0 0 1 1 ? 2099";
    }
}
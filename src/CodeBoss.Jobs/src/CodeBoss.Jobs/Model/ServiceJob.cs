using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using CronExpressionDescriptor;

namespace CodeBoss.Jobs.Model
{
    public class ServiceJob
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
        
        public string Description { get; set; }
        
        [MaxLength( 260 )]
        public string Assembly { get; set; }
        
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
        public string LastStatus { get; set; }
        
        /// <summary>
        /// Gets or sets the status message that was returned the last time that the job was run. In most cases this will be used
        /// in the event of an exception to return the exception message.
        /// </summary>
        public string LastStatusMessage { get; set; }
        
        
        public Dictionary<string, string> JobParameters { get; set; }
        
        /// <summary>
        /// Gets or sets a comma delimited list of email address that should receive notification emails for this job. Notification
        /// emails are sent to these email addresses based on the completion status of the Job and the <see cref="JobNotificationStatus"/>
        /// property of this job.
        /// </summary>
        [MaxLength( 1000 )]
        public string NotificationEmails { get; set; }
        
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
        [Required]
        public JobNotificationStatus NotificationStatus { get; set; }
        
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
        /// The never scheduled cron expression. This will only fire the job in the year 2200. This is useful for jobs
        /// that should be run only on demand, such as rebuilding Streak data.
        /// </summary>
        public static string NeverScheduledCronExpression = "0 0 0 1 1 ? 2200";
    }
}
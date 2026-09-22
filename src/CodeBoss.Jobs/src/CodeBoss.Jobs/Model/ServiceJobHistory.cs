using System;
using System.ComponentModel.DataAnnotations;

namespace CodeBoss.Jobs.Model;

public class ServiceJobHistory
{
    [Key]
    public int Id { get; set; }
    public int ServiceJobId { get; set; }
    
    /// <summary>
    /// Gets or sets the date and time that the Job started.
    /// </summary>
    /// <value>
    /// A <see cref="System.DateTime"/> representing the date and time that the Job started
    /// </value>
    public DateTime? StartDateTime { get; set; }
    public DateTime? StopDateTime { get; set; }
    
    /// <summary>
    /// Gets or sets the completion status that was returned by the Job.
    /// </summary>
    /// <value>
    /// A <see cref="System.String"/> containing the status that was returned by the Job.
    /// </value>
    [MaxLength( 50 )]
    public string? Status { get; set; }
    
    /// <summary>
    /// Gets or sets the status message that was returned by the job. In most cases this will be used
    /// in the event of an exception to return the exception message.
    /// </summary>
    /// <value>
    /// A <see cref="System.String"/> representing the Status Message that returned by the job.
    /// </value>
    public string? StatusMessage { get; set; }
    
    /// <summary>
    /// 1 for the scheduled fire, 2 and above for retries of that same fire.
    /// </summary>
    public int Attempt { get; set; } = 1;

    /// <summary>
    /// Quartz's <c>IJobExecutionContext.FireInstanceId</c> — unique per fire across the cluster.
    /// Used to correlate the start row with its completion, so a crash mid-run leaves a visible
    /// row with no stop time rather than nothing at all.
    /// </summary>
    [MaxLength(100)]
    public string? FireInstanceId { get; set; }

    /// <summary>Which scheduler instance ran it. Meaningful in a clustered deployment.</summary>
    [MaxLength(100)]
    public string? SchedulerInstanceId { get; set; }

    /// <summary>Wall-clock duration of the run.</summary>
    public int? DurationMs { get; set; }

    /// <summary>Exception type name when the run failed.</summary>
    [MaxLength(260)]
    public string? ExceptionType { get; set; }

    /// <summary>Stack trace when the run failed.</summary>
    public string? StackTrace { get; set; }

    #region Navigation Properties

    /// <summary>
    /// Gets or sets the ServiceJob <see cref="ServiceJob" /> that this ServiceJobHistory provides a History value for.
    /// </summary>
    /// <value>
    /// The service job.
    /// </value>
    public virtual ServiceJob? ServiceJob { get; set; }

    #endregion
}
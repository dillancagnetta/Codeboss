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
    public string Status { get; set; }
    
    /// <summary>
    /// Gets or sets the status message that was returned by the job. In most cases this will be used
    /// in the event of an exception to return the exception message.
    /// </summary>
    /// <value>
    /// A <see cref="System.String"/> representing the Status Message that returned by the job.
    /// </value>
    public string StatusMessage { get; set; }
    
    #region Navigation Properties

    /// <summary>
    /// Gets or sets the ServiceJob <see cref="ServiceJob" /> that this ServiceJobHistory provides a History value for.
    /// </summary>
    /// <value>
    /// The service job.
    /// </value>
    public virtual ServiceJob ServiceJob { get; set; }

    #endregion
}
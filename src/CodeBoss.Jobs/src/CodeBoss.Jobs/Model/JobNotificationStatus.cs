namespace CodeBoss.Jobs.Model;

public enum JobNotificationStatus
{
    /// <summary>
    /// Notifications should be sent when a job completes with any notification status.
    /// </summary>
    All = 1,

    /// <summary>
    /// Notification should be sent when the job has completed successfully.
    /// </summary>
    /// 
    Success = 2,

    /// <summary>
    /// Notification should be sent when the job has completed with an error status.
    /// </summary>
    Error = 3,

    /// <summary>
    /// Notifications should not be sent when this job completes with any status.
    /// </summary>
    None = 4
}
namespace CodeBoss.Jobs.Model;

/// <summary>
/// Outcome of a single job execution. Persisted to <see cref="ServiceJob.LastStatus"/> and
/// <see cref="ServiceJobHistory.Status"/> by name — every member name fits the 50-character column.
///
/// <para>These replace the free-text statuses that each consumer used to invent ("Running",
/// "Success", "Exception"), which made "show me failed jobs" a string-matching exercise.</para>
/// </summary>
public enum JobRunStatus
{
    /// <summary>Never run.</summary>
    None = 0,

    /// <summary>Scheduled but not yet started.</summary>
    Scheduled = 1,

    /// <summary>Currently executing.</summary>
    Running = 2,

    /// <summary>Completed without throwing.</summary>
    Succeeded = 3,

    /// <summary>Threw. See the status message for the exception.</summary>
    Failed = 4,

    /// <summary>Exceeded its configured timeout.</summary>
    TimedOut = 5,

    /// <summary>Failed, and a retry has been scheduled.</summary>
    Retrying = 6,

    /// <summary>Could not be scheduled at all — unresolvable type, bad cron, scheduler error.</summary>
    SchedulingError = 7,

    /// <summary>A trigger listener vetoed the fire; the job body never ran.</summary>
    Vetoed = 8,
}

namespace CodeBoss.Jobs.Jobs;

/// <summary>
/// Canonical values written to <see cref="Model.ServiceJob.LastStatus"/>, which is a
/// <c>[MaxLength(50)]</c> column. Never write a free-text message (for example an exception
/// message) into that column — it belongs in <see cref="Model.ServiceJob.LastStatusMessage"/>.
/// </summary>
public static class JobStatusText
{
    /// <summary>The job could not be scheduled (bad type, bad cron, scheduler error).</summary>
    public const string ErrorScheduling = "Error scheduling Job";

    /// <summary>An already-scheduled job could not be re-scheduled after its definition changed.</summary>
    public const string ErrorRescheduling = "Error re-scheduling Job";

    /// <summary>True when <paramref name="status"/> is one of the scheduling-error statuses.</summary>
    public static bool IsSchedulingError(string status) =>
        status == ErrorScheduling || status == ErrorRescheduling;
}

namespace CodeBoss.Jobs.Jobs;

/// <summary>
/// Quartz job-group names owned by the library.
/// </summary>
public static class JobGroups
{
    /// <summary>
    /// Group for the library's own infrastructure jobs (<see cref="JobPulse"/>,
    /// <see cref="MultiTenantJobPulse"/>). Jobs in this group are skipped by the pulse's
    /// reconciliation and by status listeners — they have no <c>ServiceJob</c> row.
    /// </summary>
    public const string System = "System";

    /// <summary>Group used for non-tenant jobs built through <c>GetJobKey(job, null)</c>.</summary>
    public const string Default = "default";

    /// <summary>Group for a tenant's jobs.</summary>
    public static string ForTenant(int tenantId) => $"tenant_{tenantId}";
}

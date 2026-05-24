using System;
using Quartz;

namespace CodeBoss.Jobs;

public class CodeBossJobsOptions
{
    public Type Repo { get; set; }
    public bool ProductionMode { get; set; } = false;
    public bool RegisteredJobListener { get; set; } = false;
    public bool IsMultiTenantMode { get; set; } = false;
    // Degree Of Parallelism
    public int ConcurrentDbOperations { get; set; } = 5;
    public int ConcurrentDbUpdateOperations { get; set; } = 3;
    public int ConcurrentSchedulerOperations { get; set; } = 10;

    /// <summary>
    /// Misfire policy applied to all built triggers. Default: DoNothing.
    /// </summary>
    public MisfirePolicy MisfirePolicy { get; set; } = MisfirePolicy.DoNothing;
}

public enum MisfirePolicy
{
    DoNothing,
    FireAndProceed,
    IgnoreMisfires
}
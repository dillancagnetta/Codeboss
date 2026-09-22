using System;
using Quartz;

namespace CodeBoss.Jobs;

public class CodeBossJobsOptions
{
    /// <summary>Default reconciliation cadence when <see cref="PulseInterval"/> is not set.</summary>
    public static readonly TimeSpan DefaultPulseInterval = TimeSpan.FromMinutes(5);

    private TimeSpan _pulseInterval = DefaultPulseInterval;

    /// <summary>
    /// Concrete <c>IServiceJobRepository</c> implementation. Required.
    /// </summary>
    public Type Repo { get; set; }

    /// <summary>
    /// How often the pulse reconciles database job rows with the scheduler. Default 5 minutes.
    /// Must be greater than zero.
    /// </summary>
    /// <remarks>
    /// The pulse trigger is registered through Quartz's own scheduling options, which default to
    /// <c>OverWriteExistingData = true</c>. Changing this value therefore takes effect on the next
    /// start even against a clustered persistent store — the stored trigger is replaced.
    /// </remarks>
    public TimeSpan PulseInterval
    {
        get => _pulseInterval;
        set => _pulseInterval = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(PulseInterval), value, "PulseInterval must be greater than zero.");
    }

    /// <summary>
    /// Run one reconciliation immediately when the scheduler starts, rather than waiting a full
    /// <see cref="PulseInterval"/>. Default true.
    /// </summary>
    public bool PulseOnStartup { get; set; } = true;

    /// <summary>
    /// Registers the consumer's <c>ICodeBossJobListener</c> against all jobs. Default false.
    /// </summary>
    public bool RegisteredJobListener { get; set; } = false;

    /// <summary>
    /// Registers <c>CodeBossJobStatusListener</c>, which persists run status and history for every
    /// job and dispatches to any registered <c>IJobRunObserver</c>s.
    ///
    /// <para>Null (the default) means <b>auto</b>: enabled unless <see cref="RegisteredJobListener"/>
    /// is set. A consumer that already has its own listener doing this bookkeeping therefore sees no
    /// change and no double-writing, while everyone else gets status tracking without opting in. Set
    /// explicitly to run both, or neither.</para>
    /// </summary>
    public bool? UseDefaultStatusListener { get; set; }

    /// <summary>Resolves <see cref="UseDefaultStatusListener"/>'s auto default.</summary>
    public bool ShouldUseDefaultStatusListener => UseDefaultStatusListener ?? !RegisteredJobListener;

    /// <summary>
    /// Schedules jobs per tenant using <c>MultiTenantJobPulse</c> instead of <c>JobPulse</c>.
    /// Requires an <c>ISimpleTenantsProvider</c> registration. Default false.
    /// </summary>
    public bool IsMultiTenantMode { get; set; } = false;

    // Degree Of Parallelism
    public int ConcurrentDbOperations { get; set; } = 5;
    public int ConcurrentDbUpdateOperations { get; set; } = 3;
    public int ConcurrentSchedulerOperations { get; set; } = 10;

    /// <summary>
    /// Misfire policy applied to all built triggers. Default: DoNothing. Overridable per job through
    /// <c>ServiceJob.JobParameters["MisfirePolicy"]</c>.
    /// </summary>
    public MisfirePolicy MisfirePolicy { get; set; } = MisfirePolicy.DoNothing;

    /// <summary>
    /// Escape hatch for Quartz configuration the library does not model: job store, serializer,
    /// clustering, scheduler id, extra listeners.
    ///
    /// <para>Invoked AFTER the library's own defaults, so anything it sets wins. In particular,
    /// calling <c>UsePersistentStore(...)</c> here replaces the default in-memory store — both write
    /// the same <c>quartz.jobStore.type</c> key and the last write wins.</para>
    ///
    /// <example>
    /// <code>
    /// o.ConfigureQuartz = q =>
    /// {
    ///     q.SchedulerId = "AUTO";
    ///     q.UsePersistentStore(store =>
    ///     {
    ///         store.UseClustering();
    ///         store.UsePostgres(ado => ado.ConnectionString = connectionString);
    ///         store.UseProperties = true;                 // required: the library writes string-only job data
    ///         store.UseSystemTextJsonSerializer();
    ///     });
    /// };
    /// </code>
    /// </example>
    /// </summary>
    public Action<IServiceCollectionQuartzConfigurator> ConfigureQuartz { get; set; }

    /// <summary>
    /// Legacy cadence switch. <c>true</c> maps to a 15 minute <see cref="PulseInterval"/>,
    /// <c>false</c> to 1 minute.
    /// </summary>
    [Obsolete("Set PulseInterval directly. ProductionMode = true maps to 15 minutes, false to 1 minute.")]
    public bool ProductionMode
    {
        get => PulseInterval >= TimeSpan.FromMinutes(15);
        set => PulseInterval = value ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(1);
    }
}

public enum MisfirePolicy
{
    DoNothing,
    FireAndProceed,
    IgnoreMisfires
}

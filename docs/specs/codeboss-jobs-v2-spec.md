# CodeBoss.Jobs v2 — Technical Specification

**Status:** Draft
**Date:** 2026-09-21
**Scope:** `src/CodeBoss.Jobs`, `tests/CodeBoss.Jobs.Tests`, and the consumer wiring in `ChurchManagerApi/src/Infrastructure/ChurchManager.Infrastructure.Shared/Jobs`.
**Origin:** Source-level comparison of CodeBoss.Jobs against [jobmaster-net](https://github.com/hugoj0s3/jobmaster-net) 0.0.11-alpha.3.

---

## 0. Summary and decision

CodeBoss.Jobs stays on Quartz. JobMaster is not adopted as a dependency: it has no tenant model, no job listener or middleware hooks, no `CancellationToken` into handlers, a bespoke DB logger instead of `ILogger`, and it is a single-maintainer alpha. Its one-off queue role is already covered in ChurchManagerApi by Wolverine with durable Postgres persistence.

What we take from JobMaster are **design ideas**: per-job retry with capped jittered backoff, per-job timeout, stable handler ids, a per-attempt execution record with a typed outcome, a run-now API, and integration tests against a real database.

What we fix in CodeBoss.Jobs are **the things ChurchManagerApi had to hack around**: sealed service members, the hard-coded in-memory store, the int `TenantId` in `JobDataMap`, tenant scope setup living in the consumer, a boolean pulse cadence, free-text statuses, and a status-length bug in `MultiTenantJobPulse`.

Work is organised in nine phases. Phases 1 to 3 are non-breaking and can ship as a patch release. Phases 4 to 8 are a major version (`CodeBoss.Jobs 2.0`). Phase 9 is test infrastructure and runs alongside everything.

| Phase | Title | Breaking | Effort |
|---|---|---|---|
| 1 | Bug fixes and virtual members — **DONE 2026-09-21** | No | S |
| 2 | Hosting and options — **DONE 2026-09-21** | No (additive) | S |
| 3 | Tenant scope in the library — **DONE 2026-09-21** | No (opt-in) | M |
| 4 | Library-owned execution status and history — **DONE 2026-09-21** | ~~Yes~~ No (see §4) | M |
| 5 | Per-job timeout and retry — **DONE 2026-09-21** | Yes (model columns) | M |
| 6 | Run-now and push sync — **DONE 2026-09-21** | No | S |
| 7 | Stable job definition ids — **DONE 2026-09-21** | No (fallback kept) | M |
| 8 | Repository interface slimming — **DONE 2026-09-21** | Yes | M |
| 9 | Integration tests with Testcontainers — **DONE 2026-09-21** | No | M |

---

## 1. Phase 1 — Bug fixes and virtual members

**Status: implemented 2026-09-21.** Test count went from 59 to 71, all passing. Two existing tests
asserted the buggy behaviour and were rewritten (`ErrorSchedulingJob_CallsUpdateStatusMessages`
verified `Times.Never`; `FailedScheduleOp_UnresolvableType_ThrowsJobExecutionException` asserted the
crash below was expected).

Two deviations from this spec as originally written, both deliberate:

- **§1.3 unified the single-tenant job key with `GetJobKey(job, null)`.** Not done. The legacy
  single-tenant key uses the job *name* as its group while `GetJobKey(job, null)` uses `"default"`.
  Unifying them would orphan every row already in a persistent job store, which is breaking. The
  legacy shape is preserved and commented.
- **The protected options property is named `JobsOptions`, not `Options`.** Naming it `Options`
  shadows the `Microsoft.Extensions.Options.Options` static class inside every subclass and breaks
  `Options.Create(...)` there. The compiler caught this while writing a subclass test.

### 1.0 Failed operations abort the sync cycle for every tenant (found during implementation)

**Not in the original spec. The most severe defect found so far.**

`MultiTenantJobPulse` used `Codeboss.Results.OperationResult<JobOperation>`, whose `Fail(string)`
factory leaves `Result` at `default` — null. The result-collection block then reads
`opResult.Result.Type` unconditionally:

```csharp
// before
return OperationResult<JobOperation>.Fail("Failed to build job detail");   // Result == null
...
switch (opResult.Result.Type)   // NullReferenceException
```

The `NullReferenceException` faults the `ActionBlock`, which faults `resultCollectionBlock.Completion`,
which `Execute` catches and rethrows as `JobExecutionException`. **One job row with a renamed class
stopped scheduling for every tenant in that cycle**, and every cycle after it, silently — the only
symptom is a pulse failure in the log.

**Fix.** A purpose-built outcome type that always carries its operation, replacing
`OperationResult<JobOperation>` in the pipeline:

```csharp
public sealed class JobOperationOutcome
{
    public JobOperation Operation { get; }   // never null
    public bool IsSuccess { get; }
    public string ErrorMessage { get; }      // null when successful

    public static JobOperationOutcome Success(JobOperation operation);
    public static JobOperationOutcome Failure(JobOperation operation, string errorMessage);
}
```

`ExecuteOperationAsync` also lost its two redundant inner `try/catch` blocks, which duplicated the
outer one.

Covered by `FailedScheduleOp_ForOneTenant_DoesNotStopOtherTenants`, which asserts tenant 2's job is
still scheduled when tenant 1's job has an unresolvable type.

### 1.1 Status column overflow in `MultiTenantJobPulse`

**Problem.** `MultiTenantJobPulse.cs:675` passes the exception message as the *status* argument:

```csharp
// current — errorMessage lands in ServiceJob.LastStatus, which is [MaxLength(50)]
var errorMessage = opResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";
await HandleAndLogError(opResult.Result.ServiceJob, errorMessage,
    new Exception(errorMessage), opResult.Result.TenantId, ct);
```

`HandleAndLogError(job, errorStatus, ex, tenantId, ct)` writes `errorStatus` to `LastStatus`. Any exception message over 50 characters fails the status write on providers that enforce length. The single-tenant `JobPulse` uses the constant `"Error scheduling Job"` here.

**Fix.** Introduce constants shared by both pulses and pass the message separately.

```csharp
// Jobs/JobStatusText.cs
namespace CodeBoss.Jobs.Jobs;

internal static class JobStatusText
{
    public const string ErrorScheduling   = "Error scheduling Job";
    public const string ErrorRescheduling = "Error re-scheduling Job";
}
```

```csharp
// MultiTenantJobPulse.cs — result collection block
if (!opResult.IsSuccess && opResult.Result.ServiceJob != null)
{
    var errorMessage = opResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";
    var status = opResult.Result.Type == OperationType.Reschedule
        ? JobStatusText.ErrorRescheduling
        : JobStatusText.ErrorScheduling;

    await WithDbSemaphore(ct, () => HandleAndLogError(
        opResult.Result.ServiceJob, status, errorMessage, opResult.Result.TenantId, ct));
}

private async Task HandleAndLogError(ServiceJob job, string status, string errorMessage, int? tenantId, CancellationToken ct)
{
    Logger.LogError("Error scheduling job {JobName} for tenant {TenantId}: {Error}", job.Name, tenantId, errorMessage);
    var message = $"Error scheduling the job: {job.Name}.\n\n{errorMessage}";
    await Repository.UpdateStatusMessagesAsync(job.Id, tenantId, message, status, ct);
}
```

Also replace the `LastStatus?.Contains("Error")` heuristic at line 686 with an equality check against the two constants.

### 1.2 Silent skip when the job type cannot be resolved

**Problem.** `JobPulse.cs:362`:

```csharp
IJobDetail jobDetail = service.BuildQuartzJob(job);
if (jobDetail == null) continue;   // nothing written to the job row
```

A renamed or moved class means the job silently never schedules. `MultiTenantJobPulse` records `"Failed to build job detail"` for the same case.

**Fix.** Record the failure in both pulses.

```csharp
if (jobDetail == null)
{
    await HandleAndLogError(job, JobStatusText.ErrorScheduling,
        $"Job type '{job.Class}, {job.Assembly}' could not be loaded.", ct);
    continue;
}
```

### 1.3 Make `ServiceJobQuartzService` extensible

**Problem.** Interface members are implicitly sealed, so `UtcServiceJobQuartzService` in ChurchManagerApi re-lists the interface and hides members with `new`. That works only because every caller uses the interface reference, and it is fragile.

**Fix.** Mark the members `virtual`, expose the misfire step as a protected hook, and stringify the data map in the base class (see 1.4).

```csharp
public class ServiceJobQuartzService(
    IDateTimeProvider dateTimeProvider,
    IOptions<CodeBossJobsOptions> options = null) : IServiceJobService
{
    protected CodeBossJobsOptions Options { get; } = options?.Value ?? new CodeBossJobsOptions();

    public virtual IJobDetail BuildQuartzJob(ServiceJob job) => BuildQuartzJob(job, tenantId: null);

    public virtual IJobDetail BuildQuartzJob(ServiceJob job, int? tenantId)
    {
        var jobType = ResolveJobType(job);
        if (jobType is null) return null;

        var map = BuildJobDataMap(job, tenantId);

        return JobBuilder.Create(jobType)
            .WithIdentity(GetJobKey(job, tenantId))
            .WithDescription(job.Id.ToString())
            .UsingJobData(map)
            .Build();
    }

    public virtual ITrigger BuildQuartzTrigger(ServiceJob job) => BuildJobTrigger(job, tenantId: null);

    public virtual ITrigger BuildJobTrigger(ServiceJob job, int? tenantId)
    {
        var jobKey = GetJobKey(job, tenantId);
        var cron = job.CronExpression.IsValidCronDescription()
            ? job.CronExpression
            : ServiceJob.NeverScheduledCronExpression;

        return TriggerBuilder.Create()
            .WithIdentity($"{jobKey.Name}_trigger", jobKey.Group)
            .WithCronSchedule(cron, x =>
            {
                x.InTimeZone(ResolveTimeZone(job));
                ApplyMisfire(job, x);
            })
            .StartNow()
            .Build();
    }

    public virtual JobKey GetJobKey(ServiceJob job, int? tenantId) =>
        tenantId.HasValue
            ? new JobKey($"{job.JobKey}_{tenantId}", $"tenant_{tenantId}")
            : new JobKey(job.JobKey.ToString(), "default");

    /// <summary>Hook: which time zone the cron fires in. Default: provider zone, else UTC.</summary>
    protected virtual TimeZoneInfo ResolveTimeZone(ServiceJob job) =>
        dateTimeProvider?.TimeZoneInfo ?? TimeZoneInfo.Utc;

    /// <summary>Hook: per-job misfire. Default: job override, else global option.</summary>
    protected virtual void ApplyMisfire(ServiceJob job, CronScheduleBuilder builder)
    {
        var policy = job.MisfirePolicy ?? Options.MisfirePolicy;
        switch (policy)
        {
            case MisfirePolicy.FireAndProceed: builder.WithMisfireHandlingInstructionFireAndProceed(); break;
            case MisfirePolicy.IgnoreMisfires: builder.WithMisfireHandlingInstructionIgnoreMisfires(); break;
            default: builder.WithMisfireHandlingInstructionDoNothing(); break;
        }
    }

    protected virtual Type ResolveJobType(ServiceJob job) => job.GetCompiledType(); // replaced in Phase 7
}
```

`ServiceJob.MisfirePolicy` becomes a nullable enum column (Phase 5 adds the migration). Until then ChurchManagerApi's `JobParameters["MisfirePolicy"]` override can be honoured inside `ApplyMisfire` by reading the parameter as a fallback.

### 1.4 Stringify the `JobDataMap` in the library

**Problem.** `map.Put("TenantId", tenantId.Value)` writes an `int`. Quartz's `useProperties=true` store mode throws on any non-string value. The consumer has to coerce every value after the fact.

**Fix.** Own it in `BuildJobDataMap`.

```csharp
public const string TenantIdKey = "TenantId";

protected virtual JobDataMap BuildJobDataMap(ServiceJob job, int? tenantId)
{
    var map = new JobDataMap();
    if (job.JobParameters is not null)
        foreach (var (key, value) in job.JobParameters)
            map[key] = value ?? string.Empty;

    if (tenantId.HasValue)
        map[TenantIdKey] = tenantId.Value.ToString(CultureInfo.InvariantCulture);

    return map;
}
```

`GetTenantIdFromQuartz` keeps working: `JobDataMap.GetIntValue` parses strings.

---

## 2. Phase 2 — Hosting and options

**Status: implemented 2026-09-21.** Jobs tests 71 → 90, all passing. No existing test needed changing.

Verified rather than assumed: Quartz 3.18's `SchedulingOptions.OverWriteExistingData` defaults to
`true` (decompiled from `Quartz.Extensions.DependencyInjection`), and `ContainerConfigurationProcessor`
passes it through to the `XMLSchedulingDataProcessor`. A changed `PulseInterval` therefore replaces the
stored trigger on the next start, so the new setting takes effect against ChurchManagerApi's clustered
store rather than being silently ignored.

Three deviations from this section as originally written, all deliberate:

- **`ConfigureQuartz` is purely additive, not an either/or.** The spec had the library call
  `UseInMemoryStore()` only when no callback was supplied. Instead the in-memory store is always
  applied as a default and the callback runs last; since both it and `UsePersistentStore` write
  `quartz.jobStore.type`, the last write wins. This keeps the callback usable for adding a listener
  without also forcing the consumer to take ownership of the job store, and it is the same mechanism
  ChurchManagerApi already relies on today.
- **The pulse trigger drops missed fires.** Added `WithMisfireHandlingInstructionNextWithRemainingCount()`,
  which the spec did not mention. Without it, a host down for an hour fires a burst of catch-up pulses
  on restart, each a full reconciliation.
- **Added an `AddCodeBossJobs(services, configure)` overload with no `IConfiguration`,** and made the
  `configuration` argument on the main overload nullable. Hosts that configure Quartz entirely in code
  have no `QuartzOptions` section to bind, and the tests need this.

Note on cadence semantics: the pulse moved from a wall-clock-aligned cron (`0 0/15 * * * ?`, firing at
:00/:15/:30/:45) to a fixed interval measured from scheduler start. For a reconciliation sweep this is
equivalent or better, since it staggers replicas rather than synchronising them.

### 2.1 Replace `ProductionMode` with an explicit cadence and expose Quartz configuration

**Problem.** `AddCodeBossJobs` hard-codes `q.UseInMemoryStore()` and picks the pulse cron from a boolean. ChurchManagerApi swaps the store by relying on `Configure<QuartzOptions>` registration order.

**Fix.**

```csharp
public class CodeBossJobsOptions
{
    public Type Repo { get; set; }
    public bool RegisteredJobListener { get; set; }
    public bool IsMultiTenantMode { get; set; }

    /// <summary>How often the pulse reconciles DB jobs with the scheduler. Default 5 min.</summary>
    public TimeSpan PulseInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Run the pulse once immediately on scheduler start. Default true.</summary>
    public bool PulseOnStartup { get; set; } = true;

    public int ConcurrentDbOperations { get; set; } = 5;
    public int ConcurrentDbUpdateOperations { get; set; } = 3;
    public int ConcurrentSchedulerOperations { get; set; } = 10;
    public MisfirePolicy MisfirePolicy { get; set; } = MisfirePolicy.DoNothing;

    /// <summary>
    /// Escape hatch for store, serializer, clustering, thread pool. Runs AFTER the library's own
    /// defaults so it can override them. When null the library uses the in-memory store.
    /// </summary>
    public Action<IServiceCollectionQuartzConfigurator> ConfigureQuartz { get; set; }

    [Obsolete("Use PulseInterval. ProductionMode=true maps to 15 minutes, false to 1 minute.")]
    public bool ProductionMode
    {
        get => PulseInterval >= TimeSpan.FromMinutes(15);
        set => PulseInterval = value ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(1);
    }
}
```

```csharp
// ConfigureServices.cs
services.AddQuartz(q =>
{
    q.UseSimpleTypeLoader();
    q.UseDefaultThreadPool(tp => tp.MaxConcurrency = options.ConcurrentSchedulerOperations);

    if (options.ConfigureQuartz is null)
        q.UseInMemoryStore();
    else
        options.ConfigureQuartz(q);

    var pulseKey = options.IsMultiTenantMode
        ? new JobKey(nameof(MultiTenantJobPulse), JobGroups.System)
        : new JobKey(nameof(JobPulse), JobGroups.System);

    if (options.IsMultiTenantMode)
        q.AddJob<MultiTenantJobPulse>(pulseKey, j => j.StoreDurably());
    else
        q.AddJob<JobPulse>(pulseKey, j => j.StoreDurably());

    q.AddTrigger(t => t
        .ForJob(pulseKey)
        .WithIdentity($"{pulseKey.Name}_trigger", JobGroups.System)
        .WithSimpleSchedule(s => s.WithInterval(options.PulseInterval).RepeatForever())
        .StartAt(options.PulseOnStartup ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow + options.PulseInterval));

    if (options.RegisteredJobListener)
        q.AddJobListener(sp => sp.GetRequiredService<ICodeBossJobListener>(), EverythingMatcher<JobKey>.AllJobs());
});
```

`StoreDurably()` on the pulse job matters for Phase 6: a durable job can be triggered on demand even with no trigger attached.

Add `public static class JobGroups { public const string System = "System"; }` and replace the six string literals `"System"` across the library.

**Consumer change (ChurchManagerApi `JobsInstaller`).** Delete `ConfigureClusteredAdoJobStore`'s `services.Configure<QuartzOptions>` layering and move the body into the option:

```csharp
services.AddCodeBossJobs(configuration, o =>
{
    o.Repo = typeof(CmServiceJobRepository);
    o.PulseInterval = environment.IsDevelopment() ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(15);
    o.RegisteredJobListener = true;
    o.IsMultiTenantMode = true;
    o.ConfigureQuartz = q =>
    {
        q.SchedulerId = "AUTO";
        q.UsePersistentStore(store =>
        {
            store.UseClustering();
            store.UsePostgres(ado =>
            {
                ado.ConnectionString = cappedConnectionString;
                ado.TablePrefix = $"{MasterDbContext.QuartzSchemaName}.{MasterDbContext.QuartzTablePrefix}";
            });
            store.PerformSchemaValidation = true;
            store.UseProperties = true;
            store.UseSystemTextJsonSerializer();
        });
    };
});
```

---

## 3. Phase 3 — Tenant scope in the library

**Status: implemented 2026-09-21.** Jobs tests 90 → 96, all passing. No existing test needed changing.

Verified rather than assumed, by decompiling Quartz 3.18:

- `MicrosoftDependencyInjectionJobFactory.InstantiateJob` calls `ConfigureScope(scope, ...)` and only
  then `CreateJob(bundle, scope.ServiceProvider)`. The ordering the whole design depends on is real.
- `ServiceCollectionExtensions.AddQuartz` registers the default factory with `TryAddSingleton`, so
  registering ours *before* that call wins without needing `services.Replace`, and a consumer's own
  factory registered earlier still wins over ours.
- `ServiceCollectionSchedulerFactory.InstantiateType<T>` resolves `T` from the container before
  falling back to the configured `quartz.scheduler.jobFactory.type`. The DI registration is therefore
  what actually takes effect, even though `AddQuartz` also writes that property.

Two deviations from this section as originally written:

- **`TryAddSingleton` before `AddQuartz`, not `services.Replace` after.** Same effect for the normal
  case, but it does not silently stomp a factory the consumer registered deliberately.
- **A missing initializer warns once, not on every fire.** Tenant jobs fire continuously; an
  unconditional warning would bury the log. Guarded with an `Interlocked.Exchange` flag.

The three behaviours worth knowing are pinned by tests that drive a real scheduler rather than
asserting registrations: the tenant is visible to a dependency's *constructor*
(`TenantContext_IsVisibleToTheJobsConstructorDependencies`), a missing initializer degrades instead of
crashing (`NoInitializerRegistered_JobStillRunsWithoutTenantContext`, also the negative control that
proves the first test measures the factory), and a throwing initializer stops the job
(`InitializerThrows_TheJobNeverRuns`).

**Problem.** `TenantAwareJobFactory` in ChurchManagerApi sets the ambient tenant before the job's DI scope resolves. This is generic behaviour that every multi-tenant consumer of the library needs.

**Fix.** Add a small interface and a job factory to the library; the consumer implements one method.

```csharp
// Abstractions/IJobTenantScopeInitializer.cs
namespace CodeBoss.Jobs.Abstractions;

/// <summary>
/// Called on the job's DI scope BEFORE the job instance and its constructor dependencies are
/// resolved. Implementations set whatever ambient tenant context the consumer's DbContext and
/// repositories read. Must be synchronous and free of I/O.
/// </summary>
public interface IJobTenantScopeInitializer
{
    void Initialize(IServiceProvider scopedProvider, int tenantId, JobKey jobKey);
}
```

```csharp
// Jobs/TenantScopedJobFactory.cs
namespace CodeBoss.Jobs.Jobs;

public sealed class TenantScopedJobFactory(
    IServiceProvider serviceProvider,
    IOptions<QuartzOptions> options,
    ILogger<TenantScopedJobFactory> logger)
    : MicrosoftDependencyInjectionJobFactory(serviceProvider, options)
{
    protected override void ConfigureScope(IServiceScope scope, TriggerFiredBundle bundle, IScheduler scheduler)
    {
        base.ConfigureScope(scope, bundle, scheduler);

        var map = bundle.JobDetail.JobDataMap;
        if (!map.ContainsKey(ServiceJobQuartzService.TenantIdKey)) return;

        var tenantId = map.GetIntValue(ServiceJobQuartzService.TenantIdKey);
        if (tenantId <= 0) return;

        var initializer = scope.ServiceProvider.GetService<IJobTenantScopeInitializer>();
        if (initializer is null)
        {
            logger.LogWarning("Tenant job {JobKey} fired but no IJobTenantScopeInitializer is registered.", bundle.JobDetail.Key);
            return;
        }

        try
        {
            initializer.Initialize(scope.ServiceProvider, tenantId, bundle.JobDetail.Key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialise tenant scope {TenantId} for job {JobKey}.", tenantId, bundle.JobDetail.Key);
            throw; // fail the fire rather than run against the wrong database
        }
    }
}
```

Registration in `AddCodeBossJobs` when `IsMultiTenantMode`:

```csharp
services.Replace(ServiceDescriptor.Singleton<IJobFactory, TenantScopedJobFactory>());
```

**Consumer change.** `TenantAwareJobFactory` is deleted and replaced by:

```csharp
public sealed class ChurchManagerJobTenantScopeInitializer : IJobTenantScopeInitializer
{
    public void Initialize(IServiceProvider sp, int tenantId, JobKey jobKey)
    {
        var setter   = sp.GetRequiredService<IAppContextSetter>();
        var accessor = sp.GetRequiredService<IAppContextAccessor>();
        var ctx = setter.InitializeAppContext(tenantId).GetAwaiter().GetResult(); // in-memory lookup, no I/O
        if (ctx.CurrentTenant is null)
            throw new InvalidOperationException($"Tenant {tenantId} not found for job {jobKey}.");
        accessor.AppContext = ctx;
    }
}
// services.AddScoped<IJobTenantScopeInitializer, ChurchManagerJobTenantScopeInitializer>();
```

Note the behaviour change: an unknown tenant now **fails the fire** instead of running with no tenant context. That is the safer default. The consumer can catch and log inside `Initialize` if it prefers the old behaviour.

---

## 4. Phase 4 — Library-owned execution status and history

**Status: implemented 2026-09-21.** Jobs tests 96 → 128, all passing.

**This phase turned out NOT to be breaking**, contrary to the plan above. The two new repository
methods ship as **default interface implementations** that map onto the existing
`UpdateStatusMessagesAsync`. An existing repository compiles untouched and still gets last-status
tracking; overriding both is what buys run timings and history rows. That keeps Phases 1–4 a single
non-breaking release.

Deviations from this section as originally written:

- **Non-breaking via default interface implementations**, as above.
- **The listener takes `IServiceScopeFactory`, not the repository and observers directly.** Quartz
  builds listeners once from the root provider, so injecting a transient or scoped repository would
  make it a captive singleton holding a database connection for the process lifetime. It now resolves
  both from a fresh scope per callback.
- **`UseDefaultStatusListener` is `bool?` with an auto default**, not a plain bool. Null means on
  unless `RegisteredJobListener` is set. Defaulting to plain `true` would have made every existing
  consumer double-write status the moment they upgraded, since their own listener already does it.
- **Every callback swallows its own exceptions.** See the bug below.

### 4.0 A throwing job listener silently stops jobs from running (found during implementation)

`GetAttempt` read the retry counter with `JobDataMap.GetString(key)`, which **throws
`KeyNotFoundException` for an absent key** rather than returning null. Every scheduled fire lacks that
key, so every call threw.

The consequence is worse than the bug. Quartz's response to a listener that throws from
`JobToBeExecuted` is to skip the job:

```
[Error] Quartz.Core.ErrorLogger: Unable to notify JobListener(s) of Job to be executed:
(Job will NOT be executed!). trigger=... job=...
 ---> System.Collections.Generic.KeyNotFoundException: Key Attempt not found
```

So a defect in *status bookkeeping* silently stopped *all real work*, with the only evidence in the
Quartz error log. It surfaced as two Phase 3 tenant tests timing out with "the job never ran".

Two fixes, both needed:

1. `GetAttempt` guards with `ContainsKey` before reading.
2. **Every `CodeBossJobStatusListener` callback now wraps its whole body in try/catch.** Bookkeeping
   is not permitted to cost a run, whatever goes wrong inside it — a failing database, a null
   reference, a bad observer.

Pinned by `RepositoryThrowingOnStart_DoesNotPreventTheJobFromRunning` and
`GetAttempt_AbsentKey_ReturnsOne`.

A related C# subtlety worth recording, hit while writing the test double: because the run-tracking
methods are **default interface implementations**, a class deriving from a base that already declares
the interface cannot `override` them. The derived type must **re-list the interface**
(`class Recording : StubRepository, IServiceJobRepository`) to rebuild the dispatch slot. This is the
same mechanism ChurchManagerApi's `UtcServiceJobQuartzService` relies on with `new`.

**Problem.** The library defines `ServiceJobHistory` and `EnableHistory` but never writes them. All status tracking lives in the consumer's `CmJobListener`, using free-text statuses (`"Running"`, `"Success"`, `"Exception"`, `"Error scheduling Job"`). Every consumer has to re-implement the same 150 lines.

**Design.** The library ships a default listener that writes status and history. The consumer plugs in only what is specific to it (notifications) through a completion hook.

### 4.1 Typed status

```csharp
// Model/JobRunStatus.cs
namespace CodeBoss.Jobs.Model;

public enum JobRunStatus
{
    None = 0,
    Scheduled = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    TimedOut = 5,
    Retrying = 6,
    SchedulingError = 7,
    Vetoed = 8,
}
```

`ServiceJob.LastStatus` stays a `string(50)` column for compatibility but is written from `JobRunStatus` names. Add a computed helper:

```csharp
public JobRunStatus LastRunStatus =>
    Enum.TryParse<JobRunStatus>(LastStatus, ignoreCase: true, out var s) ? s : JobRunStatus.None;
```

### 4.2 Per-attempt execution record

Extend `ServiceJobHistory` rather than adding a table:

```csharp
public class ServiceJobHistory
{
    [Key] public int Id { get; set; }
    public int ServiceJobId { get; set; }
    public DateTime? StartDateTime { get; set; }
    public DateTime? StopDateTime { get; set; }
    [MaxLength(50)] public string Status { get; set; }
    public string StatusMessage { get; set; }

    // new
    public int Attempt { get; set; } = 1;                 // 1 = scheduled fire, 2+ = retries
    [MaxLength(100)] public string FireInstanceId { get; set; }  // Quartz context.FireInstanceId
    [MaxLength(100)] public string SchedulerInstanceId { get; set; } // which cluster node ran it
    public int? DurationMs { get; set; }
    public string ExceptionType { get; set; }
    public string StackTrace { get; set; }

    public virtual ServiceJob ServiceJob { get; set; }
}
```

### 4.3 Repository additions

```csharp
public record JobRef(int Id, int? TenantId);

public record JobRunStarted(JobRef Job, string FireInstanceId, string SchedulerInstanceId, int Attempt, DateTime StartedUtc);

public record JobRunCompleted(
    JobRef Job, string FireInstanceId, int Attempt,
    JobRunStatus Status, string Message, TimeSpan Duration, DateTime CompletedUtc,
    Exception Exception = null);

public interface IServiceJobRepository
{
    // existing members retained in Phase 4; consolidated in Phase 8
    Task MarkRunStartedAsync(JobRunStarted run, CancellationToken ct = default);
    Task MarkRunCompletedAsync(JobRunCompleted run, CancellationToken ct = default);
}
```

Default semantics for `MarkRunCompletedAsync`, documented on the interface so every implementation matches:

- Set `LastRunDateTime`, `LastRunDurationSeconds`, `LastStatus`, `LastStatusMessage`.
- Set `LastSuccessfulRunDateTime` only when `Status == Succeeded`.
- If `EnableHistory`, upsert the history row keyed by `FireInstanceId` (created by `MarkRunStartedAsync`) and trim to `HistoryCount` rows.

### 4.4 Default listener

```csharp
// Jobs/CodeBossJobStatusListener.cs
public class CodeBossJobStatusListener(
    IServiceJobRepository repository,
    IEnumerable<IJobRunObserver> observers,
    IDateTimeProvider clock,
    ILogger<CodeBossJobStatusListener> logger) : ICodeBossJobListener
{
    public string Name => nameof(CodeBossJobStatusListener);

    public async Task JobToBeExecuted(IJobExecutionContext ctx, CancellationToken ct = default)
    {
        if (!TryGetRef(ctx, out var job)) return;
        var started = new JobRunStarted(job, ctx.FireInstanceId, ctx.Scheduler.SchedulerInstanceId,
            ctx.GetAttempt(), clock.Now);
        await repository.MarkRunStartedAsync(started, ct);
    }

    public Task JobExecutionVetoed(IJobExecutionContext ctx, CancellationToken ct = default) =>
        TryGetRef(ctx, out var job)
            ? repository.MarkRunCompletedAsync(new JobRunCompleted(job, ctx.FireInstanceId, ctx.GetAttempt(),
                JobRunStatus.Vetoed, "Vetoed by a trigger listener", TimeSpan.Zero, clock.Now), ct)
            : Task.CompletedTask;

    public async Task JobWasExecuted(IJobExecutionContext ctx, JobExecutionException ex, CancellationToken ct = default)
    {
        if (!TryGetRef(ctx, out var job)) return;

        var root = ex.Unwrap();
        var status = root switch
        {
            null                     => JobRunStatus.Succeeded,
            JobTimeoutException      => JobRunStatus.TimedOut,
            _                        => JobRunStatus.Failed,
        };

        var message = root is null
            ? (ctx.JobInstance as CodeBossJob)?.Result ?? ctx.Result as string ?? string.Empty
            : root.Message;

        var completed = new JobRunCompleted(job, ctx.FireInstanceId, ctx.GetAttempt(),
            status, message, ctx.JobRunTime, clock.Now, root);

        await repository.MarkRunCompletedAsync(completed, ct);

        foreach (var observer in observers)
        {
            try { await observer.OnCompletedAsync(ctx, completed, ct); }
            catch (Exception oex) { logger.LogError(oex, "IJobRunObserver {Observer} failed", observer.GetType().Name); }
        }
    }

    private static bool TryGetRef(IJobExecutionContext ctx, out JobRef job)
    {
        job = default;
        if (ctx.JobDetail.Key.Group == JobGroups.System) return false;
        job = new JobRef(ctx.GetJobIdFromQuartz(), ctx.GetTenantIdFromQuartz());
        return true;
    }
}
```

```csharp
// Abstractions/IJobRunObserver.cs
public interface IJobRunObserver
{
    Task OnCompletedAsync(IJobExecutionContext context, JobRunCompleted run, CancellationToken ct);
}
```

`ex.Unwrap()` is a small extension that performs the `SchedulerException` / single-item `AggregateException` drilling currently in `CmJobListener.GetExceptionToLog`.

Registration: `RegisteredJobListener` becomes `UseDefaultStatusListener` (default `true`). Consumers can still register their own `ICodeBossJobListener` and set the flag to `false`.

**Consumer change.** `CmJobListener` shrinks to an `IJobRunObserver` that only decides whether to send a notification. `CmServiceJobRepository` implements the two new methods with the tenant `DbContext` factory. Roughly 120 lines removed from ChurchManagerApi.

---

## 5. Phase 5 — Per-job timeout and retry

**Status: implemented 2026-09-21.** Jobs tests 128 → 147, all passing. Breaking only in the sense that
`ServiceJob` gains columns, so consumers need an EF migration. No API breaks.

Deviations from this section as originally written:

- **Uncooperative jobs are reported honestly, not as timeouts.** The spec implied a job that ignores
  its token could still be recorded as `TimedOut`. It cannot be aborted, so it genuinely completes its
  work; recording a timeout would be a lie. It is recorded as `Succeeded` with a warning naming the
  overrun and telling the author to pass the token down. Pinned by
  `UncooperativeJob_RunsToCompletion_AndIsReportedHonestly`.
- **A bare `OperationCanceledException` is NOT mapped to `TimedOut`.** The spec's listener did that.
  The scheduler cancels running jobs during shutdown, so it would have recorded every job interrupted
  by a deploy as too slow. Only the library's own `JobTimeoutException` means the deadline was missed.
- **No explicit `JobExecutionException` wrapping in `CodeBossJob`.** The spec added one; Quartz's
  `JobRunShell` already wraps whatever the job throws, so the extra wrap would only have added a layer
  for `Unwrap` to strip.
- **`RetryPolicy.Delay` clamps the exponent** at 2^32 before applying the cap, so a large attempt
  count cannot overflow to infinity. Pinned by `Delay_LargeAttemptCount_DoesNotOverflowToInfinity`.
- **The retry trigger name carries a GUID suffix.** Two failures of the same attempt number (a retry
  that itself misfires and is re-run) would otherwise collide on trigger key.

Both defects noted in the JobMaster comparison are avoided and pinned by tests: the backoff is capped
(`Delay_IsCapped`), jittered (`Delay_IsJittered`), and the first retry waits a full base delay rather
than half of one (`Delay_FirstRetryWaitsAFullBaseDelay_NotHalfOfOne` — JobMaster's exponent starts
at −1).

A trap worth recording, hit while writing the tests: `CodeBossJob` loads its definition through the
**tenant** overload `GetByIdAsync(id, tenantId, ct)` even in single-tenant mode, passing a null tenant
id. A repository that only implements the non-tenant overload returns null, and then every per-job
setting — timeout, retry budget — silently reverts to its default with no error. Phase 8's interface
consolidation removes this class of bug.

### 5.1 Model columns

```csharp
public class ServiceJob
{
    // ... existing ...

    /// <summary>Hard timeout for one execution. Null = no timeout.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Retries after a failure or timeout before giving up until the next cron fire. Default 0.</summary>
    public int MaxRetries { get; set; } = 0;

    /// <summary>Base delay for exponential backoff. Default 30 s.</summary>
    public int RetryBackoffBaseSeconds { get; set; } = 30;

    /// <summary>Cap on any single backoff delay. Default 15 min.</summary>
    public int RetryBackoffMaxSeconds { get; set; } = 900;

    /// <summary>Per-job misfire override. Null = use CodeBossJobsOptions.MisfirePolicy.</summary>
    public MisfirePolicy? MisfirePolicy { get; set; }

    /// <summary>Prevent overlapping executions of this job across the cluster. Default true.</summary>
    public bool DisallowConcurrentExecution { get; set; } = true;
}
```

`DisallowConcurrentExecution` is applied in `BuildQuartzJob` via `JobBuilder.DisallowConcurrentExecution(job.DisallowConcurrentExecution)`. With the clustered store Quartz enforces it across nodes. This replaces the class-level attribute, which cannot vary per job row.

### 5.2 Timeout in `CodeBossJob`

```csharp
public sealed class JobTimeoutException(string jobName, TimeSpan timeout)
    : JobExecutionException($"Job '{jobName}' exceeded its timeout of {timeout}.")
{
    public TimeSpan Timeout { get; } = timeout;
}

private async Task ExecuteInternal(IJobExecutionContext context)
{
    await InitializeFromJobContext(context);

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
    var timeout = ServiceJob?.TimeoutSeconds is > 0 ? TimeSpan.FromSeconds(ServiceJob.TimeoutSeconds.Value) : (TimeSpan?)null;
    if (timeout is not null) cts.CancelAfter(timeout.Value);

    try
    {
        Logger?.LogInformation("Executing job {JobName} (attempt {Attempt})", ServiceJobName, context.GetAttempt());
        await Execute(cts.Token);
    }
    catch (OperationCanceledException) when (timeout is not null && cts.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
    {
        throw new JobTimeoutException(ServiceJobName, timeout.Value);
    }
    catch (Exception e) when (e is not JobExecutionException)
    {
        Logger?.LogError(e, "Job {JobName} failed", ServiceJobName);
        throw new JobExecutionException(e); // Quartz only treats JobExecutionException specially
    }
}
```

The `JobExecutionException` wrap is important: Quartz logs and swallows other exception types, but a `JobExecutionException` reaches listeners with the cause intact and lets retry logic inspect it.

### 5.3 Retry via a one-shot Quartz trigger

Retry is scheduled by the status listener, not inside the job, so it survives node failure (the trigger lives in the clustered store).

```csharp
// Jobs/RetryPolicy.cs
public static class RetryPolicy
{
    /// <summary>Exponential backoff with full jitter, capped. attempt is 1-based (1 = first retry).</summary>
    public static TimeSpan Delay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay, Random rng = null)
    {
        rng ??= Random.Shared;
        var exp = Math.Min(baseDelay.TotalSeconds * Math.Pow(2, attempt - 1), maxDelay.TotalSeconds);
        return TimeSpan.FromSeconds(rng.NextDouble() * exp); // full jitter: [0, exp)
    }
}
```

```csharp
// in CodeBossJobStatusListener.JobWasExecuted, after computing `status`
if (status is JobRunStatus.Failed or JobRunStatus.TimedOut)
{
    var serviceJob = (ctx.JobInstance as CodeBossJob)?.ServiceJobSnapshot; // exposed by CodeBossJob
    var attempt = ctx.GetAttempt();
    if (serviceJob is not null && attempt <= serviceJob.MaxRetries)
    {
        var delay = RetryPolicy.Delay(attempt,
            TimeSpan.FromSeconds(serviceJob.RetryBackoffBaseSeconds),
            TimeSpan.FromSeconds(serviceJob.RetryBackoffMaxSeconds));

        var retryTrigger = TriggerBuilder.Create()
            .ForJob(ctx.JobDetail.Key)
            .WithIdentity($"{ctx.JobDetail.Key.Name}_retry_{attempt + 1}", ctx.JobDetail.Key.Group)
            .UsingJobData(JobDataKeys.Attempt, (attempt + 1).ToString(CultureInfo.InvariantCulture))
            .StartAt(DateTimeOffset.UtcNow + delay)
            .WithSimpleSchedule(s => s.WithRepeatCount(0).WithMisfireHandlingInstructionFireNow())
            .Build();

        await ctx.Scheduler.ScheduleJob(retryTrigger, ct);
        status = JobRunStatus.Retrying;
        message = $"{message} — retry {attempt + 1}/{serviceJob.MaxRetries + 1} in {delay:mm\\:ss}";
    }
}
```

```csharp
// QuartzExtensionMethods.cs
public static class JobDataKeys { public const string Attempt = "Attempt"; }

public static int GetAttempt(this IJobExecutionContext ctx) =>
    ctx.MergedJobDataMap.TryGetString(JobDataKeys.Attempt, out var s) && int.TryParse(s, out var n) ? n : 1;
```

`MergedJobDataMap` merges trigger data over job data, so the attempt counter rides on the retry trigger and the base job data stays untouched. Retry triggers are one-shot and are removed by Quartz after firing. If the cron fires while a retry is pending and `DisallowConcurrentExecution` is on, Quartz serialises them.

---

## 6. Phase 6 — Run-now and push sync

**Status: implemented 2026-09-21.** Jobs tests 147 → 157, all passing.

Deviations and additions:

- **`CodeBossJob.Parameters` was added, and it is what makes the feature real.** The spec passed a
  `JobDataMap` to `TriggerJob` but gave the job body no way to read it: `Execute(ct)` receives no
  context, and `ServiceJob.JobParameters` holds only the stored values, not the one-off overrides.
  Without this, `RunNowAsync`'s parameter argument was inert and the test asserting it was vacuous.
  Jobs now read the MERGED map, where trigger data wins over job data, so an override is visible for
  that run only and the stored definition is untouched.
- **`RunNowAsync` uses `storeNonDurableWhileAwaitingScheduling: true`** rather than adding the job
  permanently, so an on-demand run of a never-scheduled job leaves no lasting scheduler state.
- **`IDateTimeProvider` became optional on `ServiceJobQuartzService`.** Every use was already
  null-guarded and falls back to UTC, so requiring the registration bought nothing but a startup
  failure in hosts that do not have one.
- **`RequestSyncAsync` warns and returns instead of throwing** when the pulse job is absent, which is
  the normal state on a host that has not started its scheduler and has an in-memory store.

The tests deliberately configure `PulseOnStartup = false` with a twelve-hour interval and a
year-2098/2099 cron, so nothing can run from a schedule. Every execution observed is necessarily the
result of an explicit command.

**Problem.** On-demand jobs use the year-2099 cron hack. Saving a job in the admin UI waits up to 15 minutes for the pulse.

```csharp
// Abstractions/IJobCommands.cs
public interface IJobCommands
{
    /// <summary>Fire a job immediately, outside its cron schedule. Works for NeverScheduled jobs too.</summary>
    Task RunNowAsync(JobRef job, IDictionary<string, string> parameters = null, CancellationToken ct = default);

    /// <summary>Ask the pulse to reconcile now instead of waiting for its interval.</summary>
    Task RequestSyncAsync(CancellationToken ct = default);

    /// <summary>Interrupt a running job (cooperative: the job's CancellationToken is signalled).</summary>
    Task<bool> InterruptAsync(JobRef job, CancellationToken ct = default);
}
```

```csharp
// Services/QuartzJobCommands.cs
public sealed class QuartzJobCommands(
    ISchedulerFactory schedulerFactory,
    IServiceJobRepository repository,
    IServiceJobService jobService,
    IOptions<CodeBossJobsOptions> options) : IJobCommands
{
    public async Task RunNowAsync(JobRef job, IDictionary<string, string> parameters = null, CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var serviceJob = await repository.GetByIdAsync(job.Id, job.TenantId, ct);
        var key = jobService.GetJobKey(serviceJob, job.TenantId);

        if (!await scheduler.CheckExists(key, ct))
        {
            // NeverScheduled or not-yet-pulsed job: add it durably so it can be triggered.
            var detail = jobService.BuildQuartzJob(serviceJob, job.TenantId)
                ?? throw new InvalidOperationException($"Job type for '{serviceJob.Name}' could not be loaded.");
            await scheduler.AddJob(detail, replace: false, storeNonDurableWhileAwaitingScheduling: true, ct);
        }

        var data = new JobDataMap();
        if (parameters is not null)
            foreach (var (k, v) in parameters) data[k] = v ?? string.Empty;

        await scheduler.TriggerJob(key, data, ct);
    }

    public async Task RequestSyncAsync(CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var pulse = options.Value.IsMultiTenantMode ? nameof(MultiTenantJobPulse) : nameof(JobPulse);
        await scheduler.TriggerJob(new JobKey(pulse, JobGroups.System), ct);
    }

    public async Task<bool> InterruptAsync(JobRef job, CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var serviceJob = await repository.GetByIdAsync(job.Id, job.TenantId, ct);
        return await scheduler.Interrupt(jobService.GetJobKey(serviceJob, job.TenantId), ct);
    }
}
```

For `Interrupt` to work, `CodeBossJob` implements `IInterruptableJob` semantics through `context.CancellationToken`, which Quartz 3 already cancels on `Interrupt`. Nothing extra is needed beyond passing that token through, which 5.2 does.

**Consumer change.** `JobsService` in ChurchManagerApi calls `RequestSyncAsync` after create, update, and delete. A `POST /jobs/{id}/run` endpoint calls `RunNowAsync`. The API host does not run the scheduler, so `IJobCommands` needs an `ISchedulerFactory` bound to the same clustered store: register Quartz with the same `ConfigureQuartz` on the API but **without** `AddQuartzHostedService`. A scheduler that is never started can still add jobs and triggers to a clustered store; the running worker picks them up.

---

## 7. Phase 7 — Stable job definition ids

**Status: implemented 2026-09-21.** Jobs tests 157 → 176, all passing.

Deviations from this section as originally written:

- **The pulses' job-type-changed check had to move onto the registry too.** The spec only replaced
  `ResolveJobType` inside the service. Both pulses independently called `activeJob.GetCompiledType()`
  to decide whether a job's class had changed; for an id-based row that returns null, the comparison
  is skipped, and the job would look permanently unchanged — it would never be rescheduled after a
  genuine class change. `IServiceJobService` therefore gained `ResolveJobType`, as a DEFAULT interface
  implementation carrying the legacy reflection behaviour so a consumer's own `IServiceJobService`
  keeps compiling and behaving as before.
- **`GetCompiledType` is NOT marked obsolete.** It is still the registry's own fallback path and the
  interface default, so obsoleting it would only produce warnings inside the library.
- **Assembly scanning guards `ReflectionTypeLoadException`.** `Assembly.GetTypes()` throws outright if
  any single type fails to load, which would take down registration for every job in the assembly over
  one unrelated bad reference. The exception's partial results are used instead. This is the same
  weakness noted as item 4 in the JobMaster review.
- **Registered types are aliased by full name as well as by id**, so a row written before the
  attribute existed resolves through the registry rather than falling through to reflection.

The scenario the phase exists for is pinned by `ARenamedJobClass_StillResolvesThroughItsStableId`,
which resolves a row whose `Assembly` is null — something reflection could never do — and by
`WithNoRegistry_LegacyRowsStillResolve`, which proves consumers who never call `AddCodeBossJob` are
unaffected.

**Problem.** `ServiceJob.Class` and `Assembly` are resolved with `Type.GetType($"{Class}, {Assembly}")`. A namespace move or project rename silently breaks every affected row.

**Design.** Jobs get a stable string id. The DB keeps storing it in `Class`; `Assembly` becomes optional. Resolution goes through a registry first, then falls back to `Type.GetType` for unmigrated rows.

```csharp
// Abstractions/JobDefinitionIdAttribute.cs
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JobDefinitionIdAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}
```

```csharp
// Abstractions/IJobTypeRegistry.cs
public interface IJobTypeRegistry
{
    Type Resolve(string idOrTypeName, string assembly = null);
    string IdFor(Type jobType);
    IReadOnlyDictionary<string, Type> All { get; }
}

internal sealed class JobTypeRegistry : IJobTypeRegistry
{
    private readonly Dictionary<string, Type> _byId = new(StringComparer.OrdinalIgnoreCase);

    public JobTypeRegistry(IEnumerable<JobTypeRegistration> registrations)
    {
        foreach (var r in registrations)
        {
            _byId[r.Id] = r.JobType;
            _byId[r.JobType.FullName!] = r.JobType;  // legacy rows that stored FullName still resolve
        }
    }

    public Type Resolve(string idOrTypeName, string assembly = null)
    {
        if (string.IsNullOrWhiteSpace(idOrTypeName)) return null;
        if (_byId.TryGetValue(idOrTypeName, out var t)) return t;
        // legacy fallback
        return assembly is null ? Type.GetType(idOrTypeName, false, true)
                                : Type.GetType($"{idOrTypeName}, {assembly}", false, true);
    }

    public string IdFor(Type jobType) =>
        jobType.GetCustomAttribute<JobDefinitionIdAttribute>()?.Id ?? jobType.FullName!;

    public IReadOnlyDictionary<string, Type> All => _byId;
}

public sealed record JobTypeRegistration(string Id, Type JobType);
```

```csharp
// ConfigureServices.cs
public static IServiceCollection AddCodeBossJob<TJob>(this IServiceCollection services) where TJob : class, ICodeBossJob
{
    var id = typeof(TJob).GetCustomAttribute<JobDefinitionIdAttribute>()?.Id ?? typeof(TJob).FullName!;
    services.AddSingleton(new JobTypeRegistration(id, typeof(TJob)));
    services.TryAddScoped<TJob>();   // ValidateOnBuild proves the job's dependencies resolve
    return services;
}

public static IServiceCollection AddCodeBossJobsFromAssembly(this IServiceCollection services, Assembly assembly)
{
    foreach (var t in assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ICodeBossJob).IsAssignableFrom(t)))
    {
        var id = t.GetCustomAttribute<JobDefinitionIdAttribute>()?.Id ?? t.FullName!;
        services.AddSingleton(new JobTypeRegistration(id, t));
        services.TryAddScoped(t);
    }
    return services;
}
```

`ServiceJobQuartzService.ResolveJobType` becomes `registry.Resolve(job.Class, job.Assembly)`. `QuartzExtensionMethods.GetCompiledType` is marked obsolete.

Usage in ChurchManagerApi:

```csharp
[JobDefinitionId("goals.recalculate-progress")]
public class RecalculateGoalProgressJob(...) : CodeBossJob(...) { ... }

// worker Program.cs replaces the eight AddScoped<...Job>() lines:
builder.Services.AddCodeBossJobsFromAssembly(typeof(RecalculateGoalProgressJob).Assembly);
```

Seeders store `"goals.recalculate-progress"` in `Class`. Existing rows keep working through the FullName alias and the `Type.GetType` fallback; a one-off migration can rewrite them at leisure.

---

## 8. Phase 8 — Repository interface slimming

**Problem.** Every method has a tenant and non-tenant overload. Consumers implement both, and `CmServiceJobRepository` has a `NotImplementedException` in `AddOrUpdateAsync` and throws `NullReferenceException` from `GetByIdAsync`.

**Design.** One method family taking `JobRef`. Null `TenantId` means single-tenant.

```csharp
public interface IServiceJobRepository
{
    Task<IReadOnlyList<ServiceJob>> GetActiveJobsAsync(int? tenantId, CancellationToken ct = default);
    Task<ServiceJob> FindAsync(JobRef job, CancellationToken ct = default);       // null when missing
    Task SetSchedulingStatusAsync(JobRef job, JobRunStatus status, string message, CancellationToken ct = default);
    Task ClearSchedulingStatusAsync(JobRef job, CancellationToken ct = default);
    Task MarkRunStartedAsync(JobRunStarted run, CancellationToken ct = default);
    Task MarkRunCompletedAsync(JobRunCompleted run, CancellationToken ct = default);
}
```

Removed: `AddOrUpdateAsync`, `DeleteAsync` (CRUD belongs to the consumer's admin service, not the scheduler), `UpdateLastStatusMessageAsync`, `UpdateStatusMessagesAsync` (replaced by `SetSchedulingStatusAsync` and the run methods), and the parallel non-tenant overloads.

`CodeBossJob.UpdateLastStatusMessage` and `UpdateStatusMessagesAsync` (the protected helpers jobs call mid-run to report progress) become:

```csharp
/// <summary>Sets the message shown as the job's result. Persisted by the status listener on completion.</summary>
protected void SetResult(string message) => Result = message;

/// <summary>Persists a progress message immediately. Costs one DB round trip; use sparingly.</summary>
protected Task ReportProgressAsync(string message, CancellationToken ct = default) =>
    Repository.SetSchedulingStatusAsync(new JobRef(ServiceJobId, TenantId), JobRunStatus.Running, message, ct);
```

A reference EF Core implementation ships in a new package `CodeBoss.Jobs.EntityFrameworkCore` with `EfServiceJobRepository<TContext>` taking a `Func<int?, CancellationToken, Task<TContext>>` context factory, so ChurchManagerApi's `CmServiceJobRepository` becomes a ten-line subclass.

---

## 9. Phase 9 — Integration tests

**Problem.** 50 unit tests, all against mocks. The risky surface (clustered Postgres store, `useProperties`, tenant job factory, retry triggers) has no coverage.

**Packages** (add to `Directory.Packages.props`): `Testcontainers.PostgreSql`, `Quartz.Serialization.SystemTextJson`, `Npgsql`.

**Schema.** Quartz does not ship its DDL in the NuGet package. Vendor `tables_postgres.sql` from the Quartz.NET repo into `tests/CodeBoss.Jobs.IntegrationTests/Sql/` as an embedded resource, plus a `service_jobs.sql` for the two library tables.

```csharp
// PostgresQuartzFixture.cs
public sealed class PostgresQuartzFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();
    public string ConnectionString => _pg.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (var script in new[] { "tables_postgres.sql", "service_jobs.sql" })
        {
            await using var cmd = new NpgsqlCommand(EmbeddedSql.Read(script), conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    public ServiceProvider BuildHost(Action<CodeBossJobsOptions> configure = null, Action<IServiceCollection> services = null)
    {
        var sc = new ServiceCollection();
        sc.AddLogging(b => b.AddDebug());
        sc.AddSingleton<IDateTimeProvider, UtcDateTimeProvider>();
        sc.AddSingleton<IServiceJobRepository, InMemoryServiceJobRepository>(); // or the EF one against the same DB
        sc.AddCodeBossJobs(new ConfigurationBuilder().Build(), o =>
        {
            o.PulseInterval = TimeSpan.FromSeconds(2);
            o.ConfigureQuartz = q =>
            {
                q.SchedulerId = "AUTO";
                q.UsePersistentStore(s =>
                {
                    s.UseClustering();
                    s.UsePostgres(p => p.ConnectionString = ConnectionString);
                    s.UseProperties = true;
                    s.UseSystemTextJsonSerializer();
                    s.PerformSchemaValidation = true;
                });
            };
            configure?.Invoke(o);
        });
        services?.Invoke(sc);
        return sc.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
```

**Test list** (each is one `[Fact]` unless noted):

1. `Pulse_schedules_active_job_and_it_fires` — seed one job with `* * * * * ?`, assert `MarkRunStartedAsync` and `MarkRunCompletedAsync(Succeeded)` within 10 s.
2. `Tenant_id_round_trips_as_string_through_useProperties` — multi-tenant mode, assert the fired job's `TenantId` equals the seeded tenant. This test fails on the current library.
3. `Tenant_scope_initializer_runs_before_job_construction` — a stub `IJobTenantScopeInitializer` records the tenant; a job with a scoped dependency asserts it saw the same value in its constructor.
4. `Failing_job_retries_with_backoff_then_fails` — `MaxRetries = 2`, job throws; assert history rows show attempts 1, 2, 3 and final `Failed`.
5. `Timeout_produces_TimedOut_status` — `TimeoutSeconds = 1`, job sleeps 5 s honouring the token.
6. `Timeout_without_cooperation_still_reports_TimedOut` — job ignores the token; assert status after the deadline.
7. `RunNow_fires_never_scheduled_job` — cron `0 0 0 1 1 ? 2099`, call `RunNowAsync`, assert one execution.
8. `RequestSync_triggers_pulse_immediately` — `PulseInterval = 10 min`, add a job, call `RequestSyncAsync`, assert scheduled within 3 s.
9. `Two_hosts_one_store_fire_each_cron_once` — build two hosts against the same container, assert exactly one `MarkRunStartedAsync` per fire over 3 fires. This is the clustering guarantee ChurchManagerApi relies on.
10. `DisallowConcurrentExecution_false_allows_overlap` — `[Theory]` over true and false with a 3 s job on a 1 s cron.
11. `Renamed_job_type_still_resolves_via_definition_id` — register a job with `[JobDefinitionId]`, seed `Class` with the id and `Assembly = null`.
12. `Unresolvable_job_type_records_SchedulingError` — seed a bogus class name, assert status. This test fails on the current single-tenant pulse.

CI: the existing GitHub Actions test job needs Docker, which `ubuntu-latest` provides. Add `dotnet test tests/CodeBoss.Jobs.IntegrationTests` as a separate step so unit tests still report when Docker is unavailable.

---

## 10. Migration checklist for ChurchManagerApi

After each library phase ships:

| Library phase | ChurchManagerApi change |
|---|---|
| 1 | Delete `StringifyJobDataMap` from `UtcServiceJobQuartzService`; change `new` to `override`; delete `ApplyPerJobMisfireOverride` once `ServiceJob.MisfirePolicy` exists |
| 2 | Move `ConfigureClusteredAdoJobStore` body into `CodeBossJobsOptions.ConfigureQuartz`; delete the `Configure<QuartzOptions>` ordering trick and its 40-line comment; replace `ProductionMode` with `PulseInterval` |
| 3 | Delete `TenantAwareJobFactory`; add `ChurchManagerJobTenantScopeInitializer` |
| 4 | Reduce `CmJobListener` to an `IJobRunObserver` that handles notifications; implement `MarkRunStartedAsync` and `MarkRunCompletedAsync` in `CmServiceJobRepository`; EF migration for new `ServiceJobHistory` columns |
| 5 | EF migration for `TimeoutSeconds`, `MaxRetries`, `RetryBackoff*`, `MisfirePolicy`, `DisallowConcurrentExecution`; set sensible values in the seeders (for example `TimeoutSeconds = 600` on the goal recalculation sweep) |
| 6 | `JobsService` calls `RequestSyncAsync` after writes; add `POST /api/jobs/{id}/run`; register Quartz on the API host without the hosted service |
| 7 | Add `[JobDefinitionId]` to all 14 jobs; replace the eight explicit `AddScoped<…Job>()` lines in the worker with `AddCodeBossJobsFromAssembly`; update seeders to store ids |
| 8 | Replace `CmServiceJobRepository` with a subclass of `EfServiceJobRepository<ChurchManagerDbContext>`; delete the `NotImplementedException` path |

---

## 11. Out of scope

- Replacing Quartz with a custom claim and bucket engine. Quartz clustering already provides the guarantee we need and it is proven in production.
- Priority lanes and worker lanes. No current job needs them.
- A DB-backed logger. `ILogger` and Serilog stay.
- Replacing Wolverine's scheduled envelopes for one-off work. That layer is correct as is.
- A dashboard package. The existing Jobs admin page in the Angular app is extended to show attempts, retries, and the run-now button.

## 12. Open questions

1. Should `MaxRetries` default to 0 (current behaviour, opt in per job) or 1? Recommendation: 0 in the library, and set per job in the seeders.
2. History trimming to `HistoryCount` on every completion is one extra `DELETE` per run. Acceptable at current volumes; revisit if a job runs more than once a minute.
3. Phase 6 registers a second, non-started Quartz scheduler on the API host. Confirm the master Postgres pool limits leave headroom for its connection.

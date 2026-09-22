# CodeBoss.Jobs

Quartz.NET-based scheduled job library. Loads job definitions from a database (`ServiceJob`), schedules them via Quartz, and re-syncs schedule periodically. Supports single-tenant and multi-tenant modes.

## Components

| File | Role |
|------|------|
| `ConfigureServices.cs` | DI entry: `AddCodeBossJobs(...)` registers Quartz, hosted service, repo, service. |
| `CodeBossJobsOptions.cs` | Options bag: repo type, mode flags, parallelism limits, `PulseInterval`, `ConfigureQuartz`. |
| `Abstractions/ICodeBossJob.cs` | Marker over `Quartz.IJob`. |
| `Abstractions/ICodeBossJobListener.cs` | Marker over `Quartz.IJobListener` (consumer-supplied). |
| `Abstractions/IServiceJobDbRepository.cs` | DB contract: 4 required members + 2 defaulted. `JobRef` carries the optional tenant. |
| `Abstractions/IServiceJobService.cs` | Builds Quartz `IJobDetail` / `ITrigger` / `JobKey` from `ServiceJob`. |
| `Abstractions/IJobTenantScopeInitializer.cs` | Consumer hook: sets ambient tenant on a job's DI scope. Multi-tenant only. |
| `Abstractions/IJobRunObserver.cs` | Consumer hook: react to a finished run (notifications, events). |
| `Abstractions/IJobCommands.cs` | Run now / sync now / interrupt. |
| `Abstractions/JobDefinitionIdAttribute.cs` | Stable id for a job class, survives rename/move. |
| `Abstractions/IJobTypeRegistry.cs` | Id → CLR type, with reflection fallback. |
| `Services/QuartzJobCommands.cs` | `IJobCommands` impl. |
| `Abstractions/JobRun.cs` | `JobRef`, `JobRunStarted`, `JobRunCompleted`. |
| `Jobs/CodeBossJobStatusListener.cs` | Default listener: persists run status + history, dispatches observers. |
| `Model/JobRunStatus.cs` | Typed run outcome, replaces free-text statuses. |
| `Jobs/RetryPolicy.cs` | Capped, jittered exponential backoff + retry-budget check. |
| `Jobs/JobTimeoutException.cs` | Raised when a job outlives `TimeoutSeconds`. |
| `Jobs/TenantScopedJobFactory.cs` | `IJobFactory` that calls the initializer before the job is resolved. |
| `Jobs/CodeBossJob.cs` | Abstract base. Resolves `ServiceJob` from DB on each execution, exposes `UpdateLastStatusMessage`. |
| `Jobs/JobPulse.cs` | Single-tenant sync job. Adds/removes/reschedules Quartz jobs from DB state. |
| `Jobs/MultiTenantJobPulse.cs` | Multi-tenant sync job. TPL Dataflow pipeline: build batch → fan-out ops → execute → collect. |
| `Services/ServiceJobQuartzService.cs` | `IServiceJobService` impl. All members `virtual`; hooks: `ResolveJobType`, `ResolveTimeZone`, `ResolveCronExpression`, `ApplyMisfire`, `BuildJobDataMap`. |
| `Jobs/JobGroups.cs` | Quartz group-name constants: `System`, `Default`, `ForTenant(id)`. |
| `Jobs/JobStatusText.cs` | Canonical `LastStatus` values (that column is `MaxLength(50)`). |
| `QuartzExtensionMethods.cs` | Helpers: `GetJobIdFromQuartz`, `GetTenantIdFromQuartz`, `GetCompiledType`, cron validation. |
| `Model/ServiceJob.cs` | Job definition entity. `JobKey` (Guid), `Class` + `Assembly` (Type.GetType lookup), `CronExpression`, `JobParameters`, plus per-job `TimeoutSeconds`, `MaxRetries`, `RetryBackoff*`, `MisfirePolicy`, `DisallowConcurrentExecution`. |
| `Model/ServiceJobHistory.cs` | Per-attempt history row: attempt, fire id, scheduler id, duration, exception. |
| `Model/JobNotificationStatus.cs` | All / Success / Error / None. |
| `Model/TenantJobBatch.cs` | DTO + result record. |

## Lifecycle

1. Host starts → `AddCodeBossJobs` reads the `QuartzOptions` config section (optional), registers Quartz with the in-memory store, and registers `JobPulse` or `MultiTenantJobPulse` as a **durable** job plus a repeating trigger on `PulseInterval` (default 5 min). `ConfigureQuartz` runs last and may replace the store.
2. `QuartzHostedService` starts scheduler with `WaitForJobsToComplete=true`.
3. Pulse job fires on cron → loads active `ServiceJob` rows → reconciles with scheduler:
   - Delete Quartz jobs not in DB.
   - Schedule new jobs (skip if `CronExpression == NeverScheduledCronExpression` / `0 0 0 1 1 ? 2099`).
   - Reschedule if cron or `JobType` changed.
4. In multi-tenant mode, `TenantScopedJobFactory` creates the job's DI scope, reads `TenantId` from the job data map and calls `IJobTenantScopeInitializer` — all BEFORE the job type is resolved from that scope.
5. Each user job derives `CodeBossJob`. On execute: pulls `ServiceJobId` from `JobDetail.Description`, optional `TenantId` from `JobDataMap`, loads `ServiceJob`, calls `Execute(ct)`.
6. User code calls `UpdateLastStatusMessage` / `UpdateStatusMessagesAsync` to persist progress.

## Job identity

Single-tenant (`ServiceJobQuartzService.BuildQuartzJob`):
- `JobKey = (job.JobKey.ToString(), group=job.Name)`

Multi-tenant (`GetJobKey`):
- With tenant: `($"{job.JobKey}_{tenantId}", $"tenant_{tenantId}")`
- Without:    `(job.JobKey.ToString(), "default")`

System jobs use group `JobGroups.System` (skipped during sync).

## JobDataMap

Every value is written as a `string`, including `TenantId`. A persistent store configured with
`quartz.jobStore.useProperties=true` throws on any non-string value, and that is the only viable
clustered configuration on .NET 9+ (no BinaryFormatter). `GetTenantIdFromQuartz` still reads it back
via `JobDataMap.GetIntValue`, which parses the string. Null parameter values are stored as
`string.Empty` so a key that was present stays present for `ContainsKey`.

## Status writes

`ServiceJob.LastStatus` is `MaxLength(50)` and only ever receives a `JobStatusText` constant. Variable
-length detail (exception messages, unresolvable type names) goes to `LastStatusMessage`. A job whose
type cannot be loaded is recorded as `ErrorScheduling` rather than skipped silently.

A failed scheduler operation in `MultiTenantJobPulse` is isolated to its own job: it never faults the
dataflow pipeline, so other tenants in the same cycle still get scheduled.

## Consumer wiring

```csharp
services.AddCodeBossJobs(configuration, opt =>
{
    opt.Repo = typeof(MyServiceJobRepository); // : IServiceJobRepository
    opt.PulseInterval = TimeSpan.FromMinutes(15);
    opt.PulseOnStartup = true;
    opt.IsMultiTenantMode = true;
    opt.RegisteredJobListener = true; // requires ICodeBossJobListener registration
    opt.ConcurrentDbOperations = 5;
    opt.ConcurrentSchedulerOperations = 10;

    // Escape hatch: runs AFTER the library defaults, so it wins.
    opt.ConfigureQuartz = q =>
    {
        q.SchedulerId = "AUTO";
        q.UsePersistentStore(store =>
        {
            store.UseClustering();
            store.UsePostgres(ado => ado.ConnectionString = connectionString);
            store.UseProperties = true;              // required: job data is string-only
            store.UseSystemTextJsonSerializer();
        });
    };
});
```

`appsettings.json` — Quartz options:
```json
"QuartzOptions": { "quartz.scheduler.instanceName": "CodeBoss" }
```

## Tenant context

Multi-tenant requires `ISimpleTenantsProvider` from `CodeBoss.MultiTenant`, and should register an
`IJobTenantScopeInitializer` (scoped):

```csharp
services.AddScoped<IJobTenantScopeInitializer, AppJobTenantScopeInitializer>();
```

`AddCodeBossJobs` registers `TenantScopedJobFactory` as `IJobFactory` via `TryAddSingleton` BEFORE
calling `AddQuartz` (Quartz registers its own default with `TryAddSingleton` too, so first wins). The
scheduler resolves `IJobFactory` from DI — `ServiceCollectionSchedulerFactory.InstantiateType<T>`
tries the container before the configured `quartz.scheduler.jobFactory.type` — so the DI registration
is what takes effect.

Ordering is the point: the factory's `ConfigureScope` runs before Quartz resolves the job type from
the scope, so a tenant-aware `DbContext` injected into the job sees the right connection string at
construction. Setting the tenant inside the job body is too late.

An initializer that throws **aborts the fire**. That is deliberate — a job that did not run is
visible on the job row, a job that quietly wrote to the wrong tenant is not. With no initializer
registered, tenant jobs still run but with no tenant context, and the factory logs one warning.

A consumer needing its own `IJobFactory` should derive from `TenantScopedJobFactory`; registering an
unrelated one before `AddCodeBossJobs` keeps it, and tenant context will not be set.
## Repository contract

Six members, four of which must be implemented:

| Member | Required | Purpose |
|---|---|---|
| `GetActiveJobsAsync(int? tenantId, ct)` | yes | definitions to reconcile |
| `FindAsync(JobRef, ct)` | yes | one definition, null when missing |
| `SetStatusAsync(JobRef, JobRunStatus, message, ct)` | yes | status + message |
| `ClearStatusAsync(JobRef, ct)` | yes | clear a resolved error |
| `MarkRunStartedAsync(JobRunStarted, ct)` | defaulted | open a history row |
| `MarkRunCompletedAsync(JobRunCompleted, ct)` | defaulted | timings + close history |

⚠ **Breaking in 2.0.** Every method used to come in a tenant/non-tenant pair. That was not merely
verbose: `CodeBossJob` loads its own definition through the TENANT overload even in single-tenant
mode, so a repository implementing only the plain one returned null and every per-job setting
(timeout, retry budget) silently reverted to its default with no error. `JobRef` carries the optional
tenant id, so there is one method per operation and that failure mode cannot occur.

CRUD (`AddOrUpdateAsync`, `DeleteAsync`) is gone. Creating and editing job rows belongs to the
consumer's admin service; the scheduler only reads definitions and writes status.

`CodeBoss.Jobs.EntityFrameworkCore` ships `EfServiceJobRepository<TContext>`, which implements all six
including history writing and trimming. A consumer supplies only
`IServiceJobDbContextFactory<TContext>` — one method routing a tenant id to a `DbContext`. The
repository disposes what the factory returns, so return a new instance.

## Run status and history

`CodeBossJobStatusListener` is registered by default and writes status and history around every job
via two repository methods:

| Method | Called | Should write |
|---|---|---|
| `MarkRunStartedAsync` | before the job body | `LastStatus = Running`, open a history row keyed by `FireInstanceId` |
| `MarkRunCompletedAsync` | after it finishes, fails or is vetoed | run timings, `LastStatus`, `LastStatusMessage`, close the history row, trim to `HistoryCount` |

Both ship as **default interface implementations** that map onto `UpdateStatusMessagesAsync`, so an
existing repository compiles unchanged and still gets last-status tracking. Override both to get run
timings and `ServiceJobHistory` rows — the defaults cannot write those.

Statuses are the `JobRunStatus` enum written by name, not free text. Every name fits the 50-character
column.

`UseDefaultStatusListener` is `bool?`. Null means auto: on unless `RegisteredJobListener` is set, so a
consumer with its own status-writing listener sees no change and no double writes.

Consumer-specific reactions (notification emails, domain events) belong in an `IJobRunObserver`, not
in a whole custom listener. All registered observers are invoked; one that throws is logged and does
not stop the others.

⚠ **Every listener callback swallows its own exceptions, deliberately.** Quartz skips a job entirely
when `JobToBeExecuted` throws, logging "(Job will NOT be executed!)". Status bookkeeping must never
cost a run. Pinned by `RepositoryThrowingOnStart_DoesNotPreventTheJobFromRunning`.

⚠ `JobDataMap.GetString` **throws** `KeyNotFoundException` for an absent key rather than returning
null. Guard with `ContainsKey`. This is exactly how the listener broke the first time.

## Timeout

`ServiceJob.TimeoutSeconds` cancels the job's `CancellationToken` at the deadline. Enforcement is
**cooperative**: a job body that does not pass its token down runs to completion regardless — there
is no safe way to abort a thread mid-work. That case is recorded as the success it actually was, with
a warning naming the overrun, rather than a fabricated `TimedOut`.

A timeout surfaces as `JobTimeoutException`, deliberately distinct from a bare
`OperationCanceledException`: the scheduler also cancels running jobs on shutdown, and "we stopped the
host" must not be recorded as "this job is too slow".

## Retry

`MaxRetries` (default 0, so no behaviour change) with capped, jittered exponential backoff from
`RetryBackoffBaseSeconds` up to `RetryBackoffMaxSeconds`.

A retry is a **one-shot trigger in the scheduler's store**, not an in-process timer. Against a
clustered persistent store it survives the node that scheduled it dying, and any node may pick it up.
The attempt counter rides on the retry trigger's own data map, read via `MergedJobDataMap`, so the
stored job detail is never mutated and a scheduled cron fire always reads as attempt 1.

Backoff is capped because uncapped doubling reaches multi-hour delays within ten attempts, long past
the job's next scheduled fire. It is jittered because jobs that fail together usually failed for the
same reason, and retrying in lockstep rebuilds the herd that caused it.

While retries remain, the run is recorded as `Retrying`; the last failure records `Failed`.

## Job identity (`ServiceJob.Class`)

Two ways a row names its class, in priority order:

1. **Stable id** — `[JobDefinitionId("goals.recalculate-progress")]` on the class, the id in
   `ServiceJob.Class`, `Assembly` left empty. Resolved through `IJobTypeRegistry`. The class can be
   renamed or moved freely.
2. **Legacy reflection** — `Class` = full type name, `Assembly` = assembly name, resolved with
   `Type.GetType`. Still the fallback, so existing rows keep working with no migration.

Register jobs so the registry knows them:

```csharp
services.AddCodeBossJob<RecalculateGoalProgressJob>();
services.AddCodeBossJobsFromAssembly(typeof(SomeJob).Assembly);
```

Registration also adds the job to DI, so `ValidateOnBuild` proves its dependencies resolve at startup
rather than at its first fire. Registered types are aliased by full name as well as by id, so a row
that predates the attribute still resolves through the registry.

Assembly scanning skips types that fail to load: a plain `Assembly.GetTypes()` throws outright if ANY
single type in the assembly is unloadable, which would take down registration for every job beside it.

Resolution goes through `IServiceJobService.ResolveJobType`, including the pulses' "did the job class
change?" check — resolving by id there too is what stops an id-based row from being seen as
permanently unchanged.

## On-demand control (`IJobCommands`)

| Method | Use |
|---|---|
| `RunNowAsync(job, parameters?)` | Fire a job outside its schedule, with optional one-off parameter overrides |
| `RequestSyncAsync()` | Reconcile immediately instead of waiting up to a full `PulseInterval` |
| `InterruptAsync(job)` | Cancel running instances; returns whether any were found |

`RunNowAsync` works for a job whose cron is `NeverScheduledCronExpression` — the on-demand idiom —
because it adds the job detail durably first if it is not already in the scheduler. This replaces
"set the cron to 2099 and edit it when you want it to run".

One-off parameters ride on the fire's trigger data, and reach the job body through
`CodeBossJob.Parameters` (the merged map, where trigger values win over the row's `JobParameters`).
The stored job definition is never mutated.

Call `RequestSyncAsync` after creating, editing or deactivating a job so the change reaches the
scheduler immediately.

`IJobCommands` is resolvable on a host that does NOT run the scheduler — an API process beside a
worker — provided both share a clustered persistent store. Each method writes to the store and the
worker acts on it. On an in-memory store the effect is confined to the calling process.

`InterruptAsync` is cooperative: it cancels the token handed to the job body. A job that ignores its
token keeps running.

## Concurrency per job

`ServiceJob.DisallowConcurrentExecution` (default true) is applied via
`JobBuilder.DisallowConcurrentExecution(bool)`, a per-row equivalent of the class-level attribute,
which cannot vary between two rows sharing a job class. Against a clustered store Quartz enforces it
across every node.

## Concurrency model

- `[DisallowConcurrentExecution]` on both pulse jobs — only one sync cycle at a time.
- Per-operation failures are carried as `JobOperationOutcome`, which always holds its `JobOperation`.
- Multi-tenant pipeline gates DB ops via `_dbSemaphore` (size = `ConcurrentDbOperations`) and scheduler ops via `_schedulerSemaphore` (size = `ConcurrentSchedulerOperations`).
- DB-update fan-in throttled by `ConcurrentDbUpdateOperations`.

## Pulse registration

Registered as `AddJob(...).StoreDurably()` + a separate `AddTrigger(...)` simple repeating trigger,
not `ScheduleJob` + cron. Two reasons:

- **Durable** — a job with no trigger is deleted unless durable. An on-demand `TriggerJob` against the
  pulse ("sync now") needs the job to exist independently of its schedule.
- **Interval, not cron** — a missed pulse is not worth replaying, since the next run reconciles the
  full current state regardless. The trigger uses
  `WithMisfireHandlingInstructionNextWithRemainingCount()` so a host that was down does not fire a
  burst of catch-up pulses on restart.

Quartz's scheduling options default to `OverWriteExistingData = true`, so changing `PulseInterval`
takes effect on the next start even against a clustered persistent store — the stored trigger is
replaced rather than kept.

## Persistence

`UseInMemoryStore()` by default — Quartz state lost on restart. Acceptable because pulse re-syncs from DB on next tick. Supply `ConfigureQuartz` to use a clustered persistent store instead; it runs after the library defaults and both write `quartz.jobStore.type`, so the last write wins. Misfire policy: global `CodeBossJobsOptions.MisfirePolicy` (default `DoNothing`), overridable per job
via `JobParameters["MisfirePolicy"]`. Time zone from `IDateTimeProvider`, fallback UTC.

## Tests

| Project | Covers |
|---|---|
| `CodeBoss.Jobs.Tests` | unit + in-memory scheduler behaviour |
| `CodeBoss.Jobs.EntityFrameworkCore.Tests` | `EfServiceJobRepository` against SQLite |
| `CodeBoss.Jobs.IntegrationTests` | clustered Quartz + EF against real PostgreSQL (Testcontainers) |

The integration suite exists because the riskiest behaviour cannot be unit tested: the clustered ADO
job store, `useProperties` serialisation, and whether two nodes sharing a store fire each trigger once
or twice. It needs a Docker daemon and runs as its own CI step so unit results still report without one.

Two traps when writing more of them:

- **Shut schedulers down with `waitForJobsToComplete: true`.** `false` returns before Quartz's threads
  stop, so a finished test's node keeps firing into the next test's truncated tables — seen as history
  rows whose start row was truncated away, and as a Postgres deadlock against `TRUNCATE`.
- **Share one `ILoggerFactory` across nodes.** Quartz installs a process-global logging hook capturing
  the first factory it sees; disposing that node breaks every later test with a disposed
  `LoggerFactory`. `NullLoggerFactory.Instance` is safe because its `Dispose` is a no-op.
- **Create the EF schema before Quartz's DDL.** `EnsureCreated` is a no-op once the database holds any
  table, so running `tables_postgres.sql` first silently skips the job tables.

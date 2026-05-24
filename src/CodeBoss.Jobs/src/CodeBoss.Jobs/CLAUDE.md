# CodeBoss.Jobs

Quartz.NET-based scheduled job library. Loads job definitions from a database (`ServiceJob`), schedules them via Quartz, and re-syncs schedule periodically. Supports single-tenant and multi-tenant modes.

## Components

| File | Role |
|------|------|
| `ConfigureServices.cs` | DI entry: `AddCodeBossJobs(...)` registers Quartz, hosted service, repo, service. |
| `CodeBossJobsOptions.cs` | Options bag: repo type, mode flags, parallelism limits. |
| `Abstractions/ICodeBossJob.cs` | Marker over `Quartz.IJob`. |
| `Abstractions/ICodeBossJobListener.cs` | Marker over `Quartz.IJobListener` (consumer-supplied). |
| `Abstractions/IServiceJobRepository.cs` | DB contract for `ServiceJob` CRUD + status updates (tenant + non-tenant). |
| `Abstractions/IServiceJobService.cs` | Builds Quartz `IJobDetail` / `ITrigger` / `JobKey` from `ServiceJob`. |
| `Jobs/CodeBossJob.cs` | Abstract base. Resolves `ServiceJob` from DB on each execution, exposes `UpdateLastStatusMessage`. |
| `Jobs/JobPulse.cs` | Single-tenant sync job. Adds/removes/reschedules Quartz jobs from DB state. |
| `Jobs/MultiTenantJobPulse.cs` | Multi-tenant sync job. TPL Dataflow pipeline: build batch → fan-out ops → execute → collect. |
| `Services/ServiceJobQuartzService.cs` | `IServiceJobService` impl. |
| `QuartzExtensionMethods.cs` | Helpers: `GetJobIdFromQuartz`, `GetTenantIdFromQuartz`, `GetCompiledType`, cron validation. |
| `Model/ServiceJob.cs` | Job definition entity. `JobKey` (Guid), `Class` + `Assembly` (Type.GetType lookup), `CronExpression`, `JobParameters` (Dictionary). |
| `Model/ServiceJobHistory.cs` | History row (consumer persists). |
| `Model/JobNotificationStatus.cs` | All / Success / Error / None. |
| `Model/TenantJobBatch.cs` | DTO + result record. |

## Lifecycle

1. Host starts → `AddCodeBossJobs` reads `QuartzOptions` config section, registers Quartz with in-memory store, schedules `JobPulse` or `MultiTenantJobPulse` with cron from `ProductionMode` flag (15min prod, 1min dev).
2. `QuartzHostedService` starts scheduler with `WaitForJobsToComplete=true`.
3. Pulse job fires on cron → loads active `ServiceJob` rows → reconciles with scheduler:
   - Delete Quartz jobs not in DB.
   - Schedule new jobs (skip if `CronExpression == NeverScheduledCronExpression` / `0 0 0 1 1 ? 2099`).
   - Reschedule if cron or `JobType` changed.
4. Each user job derives `CodeBossJob`. On execute: pulls `ServiceJobId` from `JobDetail.Description`, optional `TenantId` from `JobDataMap`, loads `ServiceJob`, calls `Execute(ct)`.
5. User code calls `UpdateLastStatusMessage` / `UpdateStatusMessagesAsync` to persist progress.

## Job identity

Single-tenant (`ServiceJobQuartzService.BuildQuartzJob`):
- `JobKey = (job.JobKey.ToString(), group=job.Name)`

Multi-tenant (`GetJobKey`):
- With tenant: `($"{job.JobKey}_{tenantId}", $"tenant_{tenantId}")`
- Without:    `(job.JobKey.ToString(), "default")`

System jobs use group `"System"` (skipped during sync).

## Consumer wiring

```csharp
services.AddCodeBossJobs(configuration, opt =>
{
    opt.Repo = typeof(MyServiceJobRepository); // : IServiceJobRepository
    opt.ProductionMode = true;
    opt.IsMultiTenantMode = true;
    opt.RegisteredJobListener = true; // requires ICodeBossJobListener registration
    opt.ConcurrentDbOperations = 5;
    opt.ConcurrentSchedulerOperations = 10;
});
```

`appsettings.json` — Quartz options:
```json
"QuartzOptions": { "quartz.scheduler.instanceName": "CodeBoss" }
```

Multi-tenant requires `ISimpleTenantsProvider` from `CodeBoss.MultiTenant`.
History/notifications: consumer registers `ICodeBossJobListener` to write `ServiceJobHistory` and send emails.

## Concurrency model

- `[DisallowConcurrentExecution]` on both pulse jobs — only one sync cycle at a time.
- Multi-tenant pipeline gates DB ops via `_dbSemaphore` (size = `ConcurrentDbOperations`) and scheduler ops via `_schedulerSemaphore` (size = `ConcurrentSchedulerOperations`).
- DB-update fan-in throttled by `ConcurrentDbUpdateOperations`.

## Persistence

`UseInMemoryStore()` — Quartz state lost on restart. Acceptable because pulse re-syncs from DB on next tick. Misfire policy: `DoNothing`. Time zone from `IDateTimeProvider`, fallback UTC.

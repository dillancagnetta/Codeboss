# CodeBoss.Jobs — Improvement Plan

Analysis of `CodeBoss.Jobs.csproj` and surrounding code. Each item tagged with severity and downstream impact.

## csproj Gaps

| Missing | Why | Downstream impact |
|---|---|---|
| `<Nullable>enable</Nullable>` | Many obviously nullable fields (`Result`, `ServiceJob`, `LastStatus`). | Annotated public API → breaking. Requires major bump. |
| `<ImplicitUsings>enable</ImplicitUsings>` | Removes `using System;` noise. | None. |
| `<TreatWarningsAsErrors>` | Library quality. | None. |
| `<GenerateDocumentationFile>true` + suppress CS1591 | XML docs already present, not emitted. | IntelliSense for consumers. |
| `<Version>` / `<PackageVersion>` | No version → defaults `1.0.0`. | NuGet identity unstable. Pin via CI. |
| `<PackageReadmeFile>` + README pack | Discoverability. | None. |
| `<RepositoryUrl>` / `<PublishRepositoryUrl>` / `<EmbedUntrackedSources>` + `Microsoft.SourceLink.GitHub` | Stack traces resolvable. | Pure-add. |
| `<Deterministic>` + `ContinuousIntegrationBuild` | Reproducible packages. | None. |
| `<EnablePackageValidation>` + baseline | Catch API breaks. | Forces discipline. |
| Multi-target `net8.0;net9.0` | LTS reach. | Dropping net8 breaks LTS consumers. |
| Real `Description` / `Title` | Currently both `"CodeBoss.Jobs"`. | NuGet listing readability. |

### Suggested csproj rewrite

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>

    <AssemblyName>CodeBoss.Jobs</AssemblyName>
    <PackageId>CodeBoss.Jobs</PackageId>
    <Title>CodeBoss.Jobs</Title>
    <Description>Database-driven Quartz.NET job scheduler with multi-tenant support.</Description>
    <Authors>CodeBoss.co</Authors>
    <PackageTags>quartz scheduler jobs multitenant codeboss</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <RepositoryUrl>https://github.com/CodeBoss-co/Codeboss-Libs</RepositoryUrl>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <IsPackable>true</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="\" Condition="Exists('README.md')" />
    <PackageReference Include="CronExpressionDescriptor" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
    <PackageReference Include="Quartz" />
    <PackageReference Include="Quartz.Extensions.Hosting" />
    <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\CodeBoss.Extensions\src\CodeBoss.Extensions\CodeBoss.Extensions.csproj" />
    <ProjectReference Include="..\..\..\CodeBoss.MultiTenant\src\CodeBoss.MultiTenant\CodeBoss.MultiTenant.csproj" />
  </ItemGroup>
</Project>
```

Add `Microsoft.SourceLink.GitHub` to `Directory.Packages.props`.

## Code Issues

### High severity

1. **Format string gap** — `Jobs/JobPulse.cs:156`
   ```csharp
   string.Format("Error scheduling the job: {0}.\n\n{2}", job.Name, job.Assembly, ex.Message);
   ```
   `{1}` slot unused → `Assembly` dropped from log. Fix:
   ```csharp
   $"Error scheduling the job: {job.Name} ({job.Assembly}).\n\n{ex.Message}"
   ```
   *Impact:* log consumers gain Assembly field.

2. **Sync-over-async** — `Jobs/CodeBossJob.cs:75`
   ```csharp
   InitializeFromJobContext(context).Wait();
   ```
   Blocks pool thread, swallows `AggregateException`, deadlock risk under sync ctx. Fix: `await InitializeFromJobContext(context);` (containing method already async).
   *Impact:* every derived job benefits. No external API change.

3. **Reschedule logic broken** — `Jobs/MultiTenantJobPulse.cs:478-487`
   Deletes job, then `GetTriggersOfJob(operation.JobKey)` on deleted key → empty → falls through to `ScheduleJob`. `RescheduleJob` branch dead.
   `Jobs/JobPulse.cs:114-115` same ordering issue (delete then reschedule). Capture triggers before delete, or call `RescheduleJob` without prior delete.
   *Impact:* original trigger key lost → history-by-trigger-key consumers break.

4. **DI lifetime mismatch** — `ConfigureServices.cs:68-69`
   ```csharp
   services.AddTransient(typeof(IServiceJobRepository), options.Repo);
   services.AddScoped<IServiceJobService, ServiceJobQuartzService>();
   ```
   Repos typically wrap Scoped `DbContext`. Currently safe by accident (Quartz scopes per execution).
   *Impact:* changing repo to Scoped aligns with EF norms. No behaviour change for Quartz path; consumers reusing repo elsewhere see expected single-context-per-scope.

### Medium severity

5. **`configure` not asserted non-null** — `ConfigureServices.cs:17-20`
   `configure?.Invoke(options)` allows null then `options.Repo` null throws cryptic. Add `ArgumentNullException.ThrowIfNull(configure)`.

6. **`MaxConcurrency=10` hard-coded ignores option** — `ConfigureServices.cs:33`
   `CodeBossJobsOptions.ConcurrentSchedulerOperations` exists but unused for thread pool. Wire: `tp.MaxConcurrency = options.ConcurrentSchedulerOperations`.
   *Impact:* tuning option finally affects Quartz pool. May change parallelism — document.

7. **Cron comment inverted** — `ConfigureServices.cs:27`
   Comment says "test mode every minute, otherwise 15min" but ternary evaluates `ProductionMode ? "...15min..." : "...1min..."`. Logic correct, comment misleading.

8. **Static mutable `NeverScheduledCronExpression`** — `Model/ServiceJob.cs:136`
   `public static string` reassignable globally. Make `const`.
   *Impact:* breaks anyone reassigning (unlikely).

9. **Exception logging loses stack** — `Jobs/CodeBossJob.cs:86`, `Jobs/JobPulse.cs:154`
   `Logger.LogError(e.Message)` → `LogError(e, "...")`.
   *Impact:* full exception in Serilog/Seq/AppInsights.

10. **Inconsistent JobKey group** — `Services/ServiceJobQuartzService.cs:13`
    Single-tenant uses `job.Name` as group; multi-tenant uses `"default"` / `"tenant_{id}"`. Standardise on `"default"` for single-tenant.
    *Impact:* one re-sync cycle deletes/re-creates jobs with new keys. Safe under in-memory store.

11. **Repo interface filename mismatch** — `Abstractions/IServiceJobDbRepository.cs` declares `IServiceJobRepository`. Rename file.

12. **Dead code** — `Jobs/MultiTenantJobPulse.cs:48-161` (commented), `547-552` (placeholder returning `(0,0)`). Delete.

13. **Duplicate repo overloads** — tenant-aware + non-tenant pairs in `IServiceJobRepository`. Collapse to single `int? tenantId = null` parameter.
    *Impact:* breaking for implementors. Major version, or ship default-arg shims.

### Low severity

14. **Lambda param shadow** — `ConfigureServices.cs:54-58` inner `q` shadows outer Quartz `q`. Rename inner `sp`.

15. **`ServiceJobName` fallback** — `Jobs/CodeBossJob.cs:41` returns `"JobPulse"` for any subclass without a `ServiceJob`. Use `GetType().Name`.

16. **`ILogger<CodeBossJob>` for all derived** — derived class names absent from log scope. Inject `ILogger<TSelf>` or `ILoggerFactory`.

17. **`[Required]` on non-nullable enum** — `ServiceJob.NotificationStatus`. Drop attribute, default to `JobNotificationStatus.None`.

18. **Misfire / retry not configurable** — `WithMisfireHandlingInstructionDoNothing` baked in. Expose via options.

19. **No persistent store option** — `UseInMemoryStore()` hard-coded. Add opt-in `UsePersistentStore` (requires `Quartz.Serialization.Json`, schema migration).

## Downstream Impact Summary

| Change | Breaks consumers? | Mitigation |
|---|---|---|
| Enable nullable | Compile warnings | Major version bump |
| Multi-target net8+net9 | No (additive) | — |
| Wire `ConcurrentSchedulerOperations` to `MaxConcurrency` | Runtime concurrency change (default 10, same) | Document |
| Repo Scoped | Fixes captive-dep risk for some consumers | Verify with apps |
| Require non-null `configure` | Fails fast at startup | Source-only |
| Collapse repo interface overloads | Yes — interface change | Major bump or default-arg shims |
| `Wait()` → `await` | No API change | Faster, no deadlock |
| Const `NeverScheduledCronExpression` | Only if reassigned | grep consumers |
| `LogError(e, ...)` | Richer log payload | Log-string parsers may need update |
| JobKey group rename | One re-sync cycle | In-memory store, safe |

## Suggested Apply Order

1. **#1, #2, #6, #9** — low risk, high value. No API change.
2. **#5, #7, #11, #12, #14, #15, #16, #17** — cleanup, no behaviour change.
3. **#3** — fix reschedule path; add tests first.
4. **#10** — coordinate with consumer apps (one-cycle key churn).
5. **#4, #8, #18, #19** — minor option additions.
6. **#13** + csproj nullable + multi-target — major version bump milestone.

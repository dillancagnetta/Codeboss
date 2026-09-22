using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Jobs;
using CodeBoss.Jobs.Model;
using Codeboss.Types;
using Microsoft.Extensions.Options;
using Quartz;

namespace CodeBoss.Jobs.Services;

/// <summary>
/// Builds Quartz <see cref="IJobDetail"/>s and <see cref="ITrigger"/>s from <see cref="ServiceJob"/> rows.
///
/// <para>Every member is <c>virtual</c> and the interesting decisions are exposed as protected hooks
/// (<see cref="ResolveJobType"/>, <see cref="ResolveTimeZone"/>, <see cref="ApplyMisfire"/>,
/// <see cref="BuildJobDataMap"/>) so a consumer can subclass and <c>override</c> rather than hide
/// members with <c>new</c>.</para>
/// </summary>
public class ServiceJobQuartzService(
    IDateTimeProvider dateTimeProvider = null,
    IOptions<CodeBossJobsOptions> options = null,
    IJobTypeRegistry jobTypeRegistry = null) : IServiceJobService
{
    /// <summary>
    /// <see cref="JobDataMap"/> key carrying the tenant id. Always stored as a <see cref="string"/>
    /// so the map is safe for a persistent store running with <c>quartz.jobStore.useProperties=true</c>.
    /// Read it back with <c>JobDataMap.GetIntValue</c>, which parses the string.
    /// </summary>
    public const string TenantIdKey = "TenantId";

    /// <summary>
    /// Optional <see cref="ServiceJob.JobParameters"/> key overriding the global
    /// <see cref="CodeBossJobsOptions.MisfirePolicy"/> for a single job. Values match
    /// <see cref="MisfirePolicy"/> member names, case-insensitively. Absent or unparseable values
    /// leave the global policy in effect.
    /// </summary>
    public const string MisfirePolicyParameterKey = "MisfirePolicy";

    /// <summary>
    /// Options the service was configured with. Never null. Deliberately NOT named "Options":
    /// that would shadow the <see cref="Microsoft.Extensions.Options.Options"/> static class inside
    /// every subclass, breaking the common <c>Options.Create(...)</c> call.
    /// </summary>
    protected CodeBossJobsOptions JobsOptions { get; } = options?.Value ?? new CodeBossJobsOptions();

    /// <summary>
    /// The configured date/time provider. Optional: with none registered, cron expressions are
    /// interpreted in UTC. Every use here is already null-guarded, so requiring the registration
    /// bought nothing but a startup failure.
    /// </summary>
    protected IDateTimeProvider DateTimeProvider { get; } = dateTimeProvider;

    // ── Job details ──────────────────────────────────────────────────────────

    public virtual IJobDetail BuildQuartzJob(ServiceJob job)
    {
        var jobType = ResolveJobType(job);
        if (jobType == null) return null;

        // NOTE: the single-tenant key deliberately uses the job NAME as its group, which is not the
        // same shape as GetJobKey(job, null) ("default"). Unifying them would orphan every row
        // already present in a persistent job store, so the legacy shape is preserved here.
        var jobKey = new JobKey(job.JobKey.ToString(), job.Name);

        return JobBuilder.Create(jobType)
            .WithIdentity(jobKey)
            .WithDescription(job.Id.ToString())
            .DisallowConcurrentExecution(job.DisallowConcurrentExecution)
            .UsingJobData(BuildJobDataMap(job, tenantId: null))
            .Build();
    }

    public virtual IJobDetail BuildQuartzJob(ServiceJob job, int? tenantId)
    {
        var jobType = ResolveJobType(job);
        if (jobType == null) return null;

        return JobBuilder.Create(jobType)
            .WithIdentity(GetJobKey(job, tenantId))
            .WithDescription(job.Id.ToString())
            .DisallowConcurrentExecution(job.DisallowConcurrentExecution)
            .UsingJobData(BuildJobDataMap(job, tenantId))
            .Build();
    }

    // ── Triggers ─────────────────────────────────────────────────────────────

    public virtual ITrigger BuildQuartzTrigger(ServiceJob job)
    {
        // Legacy identity shape — see the note in BuildQuartzJob(ServiceJob).
        return TriggerBuilder.Create()
            .WithIdentity($"{job.JobKey}-trigger", job.Name)
            .WithCronSchedule(ResolveCronExpression(job), x =>
            {
                x.InTimeZone(ResolveTimeZone(job));
                ApplyMisfire(job, x);
            })
            .StartNow()
            .Build();
    }

    public virtual ITrigger BuildJobTrigger(ServiceJob job, int? tenantId)
    {
        var jobKey = GetJobKey(job, tenantId);

        return TriggerBuilder.Create()
            .WithIdentity($"{jobKey.Name}_trigger", jobKey.Group)
            .WithCronSchedule(ResolveCronExpression(job), x =>
            {
                x.InTimeZone(ResolveTimeZone(job));
                ApplyMisfire(job, x);
            })
            .StartNow()
            .Build();
    }

    public virtual JobKey GetJobKey(ServiceJob job, int? tenantId) =>
        tenantId.HasValue
            ? new JobKey($"{job.JobKey}_{tenantId}", JobGroups.ForTenant(tenantId.Value))
            : new JobKey(job.JobKey.ToString(), JobGroups.Default);

    // ── Hooks ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the CLR type backing a job row. Returns null when the type cannot be found; callers
    /// record that as a scheduling error rather than skipping silently.
    ///
    /// <para>Prefers the <see cref="IJobTypeRegistry"/>, which maps a stable
    /// <see cref="JobDefinitionIdAttribute"/> id and survives the class being renamed or moved. Falls
    /// back to the legacy <c>Type.GetType</c> lookup for rows written before ids existed.</para>
    /// </summary>
    public virtual Type ResolveJobType(ServiceJob job)
    {
        if (job is null) return null;

        return jobTypeRegistry is not null
            ? jobTypeRegistry.Resolve(job.Class, job.Assembly)
            : job.GetCompiledType();
    }

    /// <summary>
    /// Time zone the cron expression is interpreted in. Defaults to the configured
    /// <see cref="IDateTimeProvider"/>'s zone, falling back to UTC. Override to pin triggers to UTC
    /// when job bodies compute their windows from <see cref="DateTime.UtcNow"/>.
    /// </summary>
    protected virtual TimeZoneInfo ResolveTimeZone(ServiceJob job) =>
        DateTimeProvider?.TimeZoneInfo ?? TimeZoneInfo.Utc;

    /// <summary>
    /// The cron expression to schedule with. An invalid expression falls back to
    /// <see cref="ServiceJob.NeverScheduledCronExpression"/> so the job is visible in the scheduler
    /// but never fires.
    /// </summary>
    protected virtual string ResolveCronExpression(ServiceJob job) =>
        !string.IsNullOrWhiteSpace(job.CronExpression) && job.CronExpression.IsValidCronDescription()
            ? job.CronExpression
            : ServiceJob.NeverScheduledCronExpression;

    /// <summary>
    /// Applies the misfire instruction. A per-job override in
    /// <see cref="ServiceJob.JobParameters"/> under <see cref="MisfirePolicyParameterKey"/> wins over
    /// the global <see cref="CodeBossJobsOptions.MisfirePolicy"/>. A missing or unparseable value
    /// never throws; the global policy simply stands.
    /// </summary>
    protected virtual void ApplyMisfire(ServiceJob job, CronScheduleBuilder builder)
    {
        switch (ResolveMisfirePolicy(job))
        {
            case MisfirePolicy.FireAndProceed:
                builder.WithMisfireHandlingInstructionFireAndProceed();
                break;
            case MisfirePolicy.IgnoreMisfires:
                builder.WithMisfireHandlingInstructionIgnoreMisfires();
                break;
            default:
                builder.WithMisfireHandlingInstructionDoNothing();
                break;
        }
    }

    /// <summary>
    /// Per-job misfire policy if set, otherwise the global option. The typed column wins over the
    /// <see cref="MisfirePolicyParameterKey"/> parameter, which predates it.
    /// </summary>
    protected MisfirePolicy ResolveMisfirePolicy(ServiceJob job)
    {
        if (job?.MisfirePolicy is { } declared) return declared;

        if (job?.JobParameters != null &&
            job.JobParameters.TryGetValue(MisfirePolicyParameterKey, out var raw) &&
            Enum.TryParse<MisfirePolicy>(raw, ignoreCase: true, out var perJob))
        {
            return perJob;
        }

        return JobsOptions.MisfirePolicy;
    }

    /// <summary>
    /// Builds the job's data map. Every value is coerced to a <see cref="string"/>: a persistent
    /// store configured with <c>useProperties=true</c> throws on any non-string value, and that is
    /// the only safe configuration for a clustered store on .NET 9+ (no BinaryFormatter). Null
    /// values become <see cref="string.Empty"/> rather than being dropped, so a key that was present
    /// stays present for <c>ContainsKey</c>.
    /// </summary>
    protected virtual JobDataMap BuildJobDataMap(ServiceJob job, int? tenantId)
    {
        var map = new JobDataMap();

        if (job?.JobParameters != null)
        {
            foreach (KeyValuePair<string, string> parameter in job.JobParameters)
            {
                map[parameter.Key] = parameter.Value ?? string.Empty;
            }
        }

        if (tenantId.HasValue)
        {
            map[TenantIdKey] = tenantId.Value.ToString(CultureInfo.InvariantCulture);
        }

        return map;
    }

    /// <summary>Determines whether the Cron Expression is valid for Quartz.</summary>
    public static bool IsValidCronDescription(string cronExpression) => cronExpression.IsValidCronDescription();
}

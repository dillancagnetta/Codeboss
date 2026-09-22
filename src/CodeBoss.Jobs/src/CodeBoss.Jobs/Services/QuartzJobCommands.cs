using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace CodeBoss.Jobs.Services;

/// <inheritdoc cref="IJobCommands"/>
public class QuartzJobCommands(
    ISchedulerFactory schedulerFactory,
    IServiceJobRepository repository,
    IServiceJobService jobService,
    IOptions<CodeBossJobsOptions> options,
    ILogger<QuartzJobCommands> logger) : IJobCommands
{
    private readonly CodeBossJobsOptions _options = options?.Value ?? new CodeBossJobsOptions();

    public virtual async Task RunNowAsync(JobRef job, IDictionary<string, string> parameters = null,
        CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);

        var definition = await repository.FindAsync(job, ct)
            ?? throw new InvalidOperationException($"No ServiceJob row for {job}.");

        var jobKey = jobService.GetJobKey(definition, job.TenantId);

        if (!await scheduler.CheckExists(jobKey, ct))
        {
            // Not scheduled. Either the pulse has not run yet, or the job is deliberately
            // never-scheduled (the on-demand idiom), in which case the pulse never adds it at all.
            var detail = jobService.BuildQuartzJob(definition, job.TenantId)
                ?? throw new InvalidOperationException(
                    $"Job type '{definition.Class}, {definition.Assembly}' could not be loaded for {job}.");

            // storeNonDurableWhileAwaitingScheduling: keep it just long enough to be triggered. It is
            // removed once the run completes, so an on-demand job leaves no permanent scheduler state.
            await scheduler.AddJob(detail, replace: true, storeNonDurableWhileAwaitingScheduling: true, ct);
        }

        await scheduler.TriggerJob(jobKey, BuildOverrides(parameters), ct);

        logger.LogInformation("Triggered {Job} ({JobKey}) on demand.", job, jobKey);
    }

    public virtual async Task RequestSyncAsync(CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var pulseKey = PulseKey();

        if (!await scheduler.CheckExists(pulseKey, ct))
        {
            logger.LogWarning("Cannot request a sync: the pulse job {JobKey} is not in the scheduler.", pulseKey);
            return;
        }

        await scheduler.TriggerJob(pulseKey, ct);

        logger.LogInformation("Requested an immediate reconciliation via {JobKey}.", pulseKey);
    }

    public virtual async Task<bool> InterruptAsync(JobRef job, CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);

        var definition = await repository.FindAsync(job, ct)
            ?? throw new InvalidOperationException($"No ServiceJob row for {job}.");

        var jobKey = jobService.GetJobKey(definition, job.TenantId);
        var interrupted = await scheduler.Interrupt(jobKey, ct);

        logger.LogInformation("Interrupt requested for {Job} ({JobKey}); running instance found: {Found}.",
            job, jobKey, interrupted);

        return interrupted;
    }

    /// <summary>The durable pulse job registered by <c>AddCodeBossJobs</c>.</summary>
    protected virtual JobKey PulseKey() =>
        _options.IsMultiTenantMode
            ? new JobKey(nameof(MultiTenantJobPulse), JobGroups.System)
            : new JobKey(nameof(JobPulse), JobGroups.System);

    /// <summary>
    /// Per-run parameter overrides. Every value is a string: a persistent store with
    /// <c>useProperties=true</c> rejects anything else.
    /// </summary>
    private static JobDataMap BuildOverrides(IDictionary<string, string> parameters)
    {
        var map = new JobDataMap();
        if (parameters is null) return map;

        foreach (var parameter in parameters)
        {
            map[parameter.Key] = parameter.Value ?? string.Empty;
        }

        return map;
    }
}

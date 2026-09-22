using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Model;
using Codeboss.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace CodeBoss.Jobs.Jobs;

/// <summary>
/// Writes run status and history for every job execution, and hands the finished run to any
/// registered <see cref="IJobRunObserver"/>s.
///
/// <para>This used to be every consumer's problem: each one wrote its own <c>IJobListener</c> with
/// the same hundred-odd lines of status bookkeeping and its own invented status strings. The library
/// now owns the bookkeeping and consumers implement only what is genuinely theirs — notifications,
/// domain events — through <see cref="IJobRunObserver"/>.</para>
///
/// <para>System jobs (the pulse) are skipped: they have no <c>ServiceJob</c> row to write to.</para>
///
/// <para>Quartz builds listeners once, from the root provider, so this type resolves the repository
/// and the observers from a FRESH SCOPE on every callback. Holding them would make a scoped or
/// transient repository a captive singleton — and a job repository usually owns a database
/// connection.</para>
///
/// <para><b>Every callback swallows its own exceptions.</b> Quartz treats a throwing
/// <c>JobToBeExecuted</c> as a reason to skip the job entirely — it logs "Unable to notify
/// JobListener(s) of Job to be executed: (Job will NOT be executed!)" and moves on. Status
/// bookkeeping failing must never stop real work from running, so nothing here is allowed to
/// escape.</para>
/// </summary>
public class CodeBossJobStatusListener(
    IServiceScopeFactory scopeFactory,
    ILogger<CodeBossJobStatusListener> logger) : ICodeBossJobListener
{
    public virtual string Name => nameof(CodeBossJobStatusListener);

    public virtual async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken ct = default)
    {
        try
        {
            if (context.GetJobRef() is not { } job) return;

            await using var scope = scopeFactory.CreateAsyncScope();

            var started = new JobRunStarted(
                job,
                context.FireInstanceId,
                context.Scheduler?.SchedulerInstanceId,
                context.GetAttempt(),
                UtcNow(scope.ServiceProvider));

            await scope.ServiceProvider.GetRequiredService<IServiceJobRepository>()
                .MarkRunStartedAsync(started, ct);
        }
        catch (Exception ex)
        {
            // MUST NOT propagate: Quartz skips the job entirely when this callback throws.
            logger.LogError(ex, "Failed to record the start of {JobKey}; the job still runs.",
                context?.JobDetail?.Key);
        }
    }

    public virtual async Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken ct = default)
    {
        try
        {
            if (context.GetJobRef() is not { } job) return;

            logger.LogDebug("{Job} ({JobKey}) was vetoed by a trigger listener.", job, context.JobDetail.Key);

            await CompleteAsync(context, job, JobRunStatus.Vetoed, "Vetoed by a trigger listener",
                TimeSpan.Zero, cause: null, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record the veto of {JobKey}.", context?.JobDetail?.Key);
        }
    }

    public virtual async Task JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException,
        CancellationToken ct = default)
    {
        try
        {
            await JobWasExecutedCore(context, jobException, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record the completion of {JobKey}.", context?.JobDetail?.Key);
        }
    }

    private async Task JobWasExecutedCore(IJobExecutionContext context, JobExecutionException jobException,
        CancellationToken ct)
    {
        if (context.GetJobRef() is not { } job) return;

        var cause = jobException?.Unwrap();
        var status = ResolveStatus(cause);
        var message = ResolveMessage(context, cause);

        if (cause is null)
        {
            logger.LogDebug("{Job} ({JobKey}) succeeded in {Duration}.", job, context.JobDetail.Key, context.JobRunTime);
        }
        else
        {
            logger.LogError(cause, "{Job} ({JobKey}) failed with {Status}.", job, context.JobDetail.Key, status);

            var retry = await TryScheduleRetryAsync(context, job, ct);
            if (retry is not null)
            {
                status = JobRunStatus.Retrying;
                message = $"{message} — retry {retry.Value.Attempt} scheduled in {retry.Value.Delay.TotalSeconds:F0}s";
            }
        }

        await CompleteAsync(context, job, status, message, context.JobRunTime, cause, ct);
    }

    /// <summary>
    /// Maps the unwrapped failure to a status. Override to recognise consumer-specific exceptions.
    /// </summary>
    /// <remarks>
    /// A bare <see cref="OperationCanceledException"/> is NOT a timeout — the scheduler cancels
    /// running jobs on shutdown too. Only the library's own <see cref="JobTimeoutException"/> means
    /// the job outlived its deadline.
    /// </remarks>
    protected virtual JobRunStatus ResolveStatus(Exception cause) => cause switch
    {
        null => JobRunStatus.Succeeded,
        JobTimeoutException => JobRunStatus.TimedOut,
        _ => JobRunStatus.Failed,
    };

    /// <summary>
    /// Schedules a one-shot retry trigger when the job has budget left, and returns its attempt
    /// number and delay. Returns null when no retry is due.
    /// </summary>
    /// <remarks>
    /// The retry is a TRIGGER in the scheduler's store, not an in-process timer: against a clustered
    /// persistent store it survives this node dying, and any node can pick it up. The attempt counter
    /// rides on the trigger's own data map, so the stored job detail is never mutated and the
    /// scheduled cron fire still reads as attempt 1.
    /// </remarks>
    protected virtual async Task<(int Attempt, TimeSpan Delay)?> TryScheduleRetryAsync(
        IJobExecutionContext context, JobRef job, CancellationToken ct)
    {
        var definition = (context.JobInstance as CodeBossJob)?.ServiceJobDefinition;
        var failedAttempt = context.GetAttempt();

        if (!RetryPolicy.ShouldRetry(definition, failedAttempt)) return null;

        var delay = RetryPolicy.DelayFor(definition, failedAttempt);
        var nextAttempt = failedAttempt + 1;
        var jobKey = context.JobDetail.Key;

        try
        {
            var trigger = TriggerBuilder.Create()
                .ForJob(jobKey)
                .WithIdentity($"{jobKey.Name}_retry_{nextAttempt}_{Guid.NewGuid():N}", jobKey.Group)
                // String value: a persistent store with useProperties=true rejects anything else.
                .UsingJobData(QuartzExtensionMethods.JobDataKeys.Attempt,
                    nextAttempt.ToString(CultureInfo.InvariantCulture))
                .StartAt(DateTimeOffset.UtcNow.Add(delay))
                .WithSimpleSchedule(s => s.WithRepeatCount(0).WithMisfireHandlingInstructionFireNow())
                .Build();

            await context.Scheduler.ScheduleJob(trigger, ct);

            logger.LogInformation(
                "{Job} ({JobKey}) failed on attempt {Attempt} of {Max}; retrying in {Delay}.",
                job, jobKey, failedAttempt, definition.MaxRetries + 1, delay);

            return (nextAttempt, delay);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not schedule a retry for {Job} ({JobKey}).", job, jobKey);
            return null;
        }
    }

    /// <summary>
    /// Text stored as the run's message: the job's own result on success, the failure message
    /// otherwise. Never null.
    /// </summary>
    protected virtual string ResolveMessage(IJobExecutionContext context, Exception cause)
    {
        if (cause is not null)
        {
            return cause is AggregateException { InnerExceptions.Count: > 1 } aggregate
                ? $"One or more exceptions occurred. First: {aggregate.InnerExceptions[0].Message}"
                : cause.Message;
        }

        return (context.JobInstance as CodeBossJob)?.Result
               ?? context.Result as string
               ?? string.Empty;
    }

    private async Task CompleteAsync(
        IJobExecutionContext context, JobRef job, JobRunStatus status, string message,
        TimeSpan duration, Exception cause, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var run = new JobRunCompleted(job, context.FireInstanceId, context.GetAttempt(),
            status, message, duration, UtcNow(scope.ServiceProvider), cause);

        try
        {
            await scope.ServiceProvider.GetRequiredService<IServiceJobRepository>()
                .MarkRunCompletedAsync(run, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record the completion of {Job} ({JobKey}).", job, context.JobDetail.Key);
        }

        foreach (var observer in scope.ServiceProvider.GetServices<IJobRunObserver>())
        {
            try
            {
                await observer.OnCompletedAsync(context, run, ct);
            }
            catch (Exception ex)
            {
                // One bad observer must not suppress the others; the job itself is already done.
                logger.LogError(ex, "Job run observer {Observer} threw for {Job}.", observer.GetType().Name, job);
            }
        }
    }

    /// <summary>
    /// Uses the consumer's clock when one is registered, so tests and non-UTC deployments stay
    /// consistent with the timestamps jobs write themselves.
    /// </summary>
    private static DateTime UtcNow(IServiceProvider provider)
    {
        var now = provider.GetService<IDateTimeProvider>()?.Now ?? DateTime.UtcNow;
        return DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }
}

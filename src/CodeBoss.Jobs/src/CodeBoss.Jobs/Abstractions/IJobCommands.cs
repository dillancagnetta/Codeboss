using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBoss.Jobs.Abstractions;

/// <summary>
/// Out-of-band operations on scheduled jobs: run one now, reconcile now, stop one that is running.
///
/// <para>Safe to resolve on a host that does not run the scheduler (an API process alongside a
/// worker), PROVIDED both share a persistent clustered job store. Each method writes to that store
/// and the worker acts on it. On an in-memory store these only affect the calling process.</para>
/// </summary>
public interface IJobCommands
{
    /// <summary>
    /// Fires a job immediately, outside its schedule.
    ///
    /// <para>Works for a job whose cron is <c>ServiceJob.NeverScheduledCronExpression</c> — the
    /// on-demand idiom — because the job detail is added durably first if it is not already present.
    /// This replaces "set the cron to the year 2099 and wait for someone to edit it".</para>
    /// </summary>
    /// <param name="job">Which job row.</param>
    /// <param name="parameters">
    /// One-off values merged over the job's own <c>JobParameters</c> for this run only. Coerced to
    /// strings, as a persistent store requires.
    /// </param>
    /// <exception cref="System.InvalidOperationException">The job row is missing, or its type cannot be loaded.</exception>
    Task RunNowAsync(JobRef job, IDictionary<string, string> parameters = null, CancellationToken ct = default);

    /// <summary>
    /// Runs the reconciliation pulse now instead of waiting up to a full
    /// <c>CodeBossJobsOptions.PulseInterval</c>. Call after creating, editing or deactivating a job so
    /// the change reaches the scheduler immediately.
    /// </summary>
    Task RequestSyncAsync(CancellationToken ct = default);

    /// <summary>
    /// Signals cancellation to every currently running instance of a job, and reports whether any
    /// were found.
    ///
    /// <para>Cooperative: it cancels the <see cref="CancellationToken"/> the job body was handed. A
    /// job that does not observe its token keeps running.</para>
    /// </summary>
    Task<bool> InterruptAsync(JobRef job, CancellationToken ct = default);
}

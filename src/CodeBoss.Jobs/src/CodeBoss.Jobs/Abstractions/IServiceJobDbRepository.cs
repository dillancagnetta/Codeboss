using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBoss.Jobs.Model;

namespace CodeBoss.Jobs.Abstractions
{
    /// <summary>
    /// Everything the scheduler needs from the job store. Deliberately small.
    ///
    /// <para><b>Breaking change in 2.0.</b> Previously every method came in a pair — one taking a
    /// tenant id and one not — which doubled the implementation surface and made it easy to implement
    /// only half. That was not a theoretical risk: a job loads its own definition through the TENANT
    /// overload even in single-tenant mode, so a repository implementing only the plain overload
    /// returned null and every per-job setting (timeout, retry budget) silently reverted to its
    /// default, with no error anywhere. <see cref="JobRef"/> carries the optional tenant id instead,
    /// so there is exactly one method per operation.</para>
    ///
    /// <para>CRUD is gone too. Creating, editing and deleting job rows belongs to the consumer's own
    /// admin service; the scheduler only ever reads definitions and writes status.</para>
    ///
    /// <para>Only four members need implementing — the two run-tracking methods have working defaults.</para>
    /// </summary>
    public interface IServiceJobRepository
    {
        /// <summary>
        /// Active job definitions to reconcile with the scheduler.
        /// </summary>
        /// <param name="tenantId">The tenant to load for, or null in single-tenant mode.</param>
        Task<IReadOnlyList<ServiceJob>> GetActiveJobsAsync(int? tenantId, CancellationToken ct = default);

        /// <summary>
        /// A single job definition, or <see langword="null"/> when the row does not exist.
        /// </summary>
        /// <remarks>
        /// Returning null rather than throwing is deliberate: callers decide whether a missing row is
        /// fatal. A job whose row was deleted mid-flight should not produce a
        /// <c>NullReferenceException</c> from the repository.
        /// </remarks>
        Task<ServiceJob> FindAsync(JobRef job, CancellationToken ct = default);

        /// <summary>
        /// Records a status and message against a job row.
        /// </summary>
        /// <param name="status">
        /// Written to <c>ServiceJob.LastStatus</c> by name. Implementations must NOT write
        /// <paramref name="message"/> there — that column is <c>MaxLength(50)</c>.
        /// </param>
        /// <param name="message">Free text for <c>ServiceJob.LastStatusMessage</c>.</param>
        Task SetStatusAsync(JobRef job, JobRunStatus status, string message, CancellationToken ct = default);

        /// <summary>
        /// Clears a previously recorded error status, once the condition that caused it is resolved.
        /// </summary>
        Task ClearStatusAsync(JobRef job, CancellationToken ct = default);

        /// <summary>
        /// Records that a run has begun. Should set <c>LastStatus</c> to
        /// <see cref="JobRunStatus.Running"/> and, when <c>ServiceJob.EnableHistory</c> is set, insert
        /// a <see cref="ServiceJobHistory"/> row keyed by <see cref="JobRunStarted.FireInstanceId"/>
        /// with no stop time. An open row is how a host that died mid-run stays visible.
        /// </summary>
        /// <remarks>The default implementation records status only, not history.</remarks>
        Task MarkRunStartedAsync(JobRunStarted run, CancellationToken ct = default)
            => SetStatusAsync(run.Job, JobRunStatus.Running, $"Started at {run.StartedUtc:u}", ct);

        /// <summary>
        /// Records that a run has finished. Should set <c>LastRunDateTime</c>,
        /// <c>LastRunDurationSeconds</c>, <c>LastStatus</c> and <c>LastStatusMessage</c>; set
        /// <c>LastSuccessfulRunDateTime</c> only when <see cref="JobRunCompleted.IsSuccess"/>; and,
        /// when <c>ServiceJob.EnableHistory</c> is set, close the history row matching
        /// <see cref="JobRunCompleted.FireInstanceId"/> and trim to <c>ServiceJob.HistoryCount</c> rows.
        /// </summary>
        /// <remarks>The default implementation records status only, not run timings or history.</remarks>
        Task MarkRunCompletedAsync(JobRunCompleted run, CancellationToken ct = default)
            => SetStatusAsync(run.Job, run.Status, run.Message ?? string.Empty, ct);
    }
}

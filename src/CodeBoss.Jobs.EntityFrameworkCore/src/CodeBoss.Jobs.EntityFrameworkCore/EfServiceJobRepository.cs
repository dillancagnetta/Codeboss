using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Model;
using Microsoft.EntityFrameworkCore;

namespace CodeBoss.Jobs.EntityFrameworkCore;

/// <summary>
/// Entity Framework Core implementation of <see cref="IServiceJobRepository"/>, including the run
/// history bookkeeping that every consumer previously had to write for itself.
///
/// <para>Supply an <see cref="IServiceJobDbContextFactory{TContext}"/> and register this as the
/// repository:</para>
/// <code>
/// services.AddScoped&lt;IServiceJobDbContextFactory&lt;AppDbContext&gt;, AppJobDbContextFactory&gt;();
/// services.AddCodeBossJobs(config, o => o.Repo = typeof(EfServiceJobRepository&lt;AppDbContext&gt;));
/// </code>
/// </summary>
public class EfServiceJobRepository<TContext>(IServiceJobDbContextFactory<TContext> contextFactory)
    : IServiceJobRepository
    where TContext : DbContext
{
    public virtual async Task<IReadOnlyList<ServiceJob>> GetActiveJobsAsync(int? tenantId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateAsync(tenantId, ct);

        // No tracking: these are read to compare against the scheduler, never written back here.
        return await db.Set<ServiceJob>()
            .AsNoTracking()
            .Where(job => job.IsActive)
            .ToListAsync(ct);
    }

    public virtual async Task<ServiceJob> FindAsync(JobRef job, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateAsync(job.TenantId, ct);

        return await db.Set<ServiceJob>()
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == job.Id, ct);
    }

    public virtual async Task SetStatusAsync(JobRef job, JobRunStatus status, string message, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateAsync(job.TenantId, ct);

        var row = await db.Set<ServiceJob>().FirstOrDefaultAsync(r => r.Id == job.Id, ct);
        if (row is null) return;

        row.LastStatus = status.ToString();
        row.LastStatusMessage = message;

        await db.SaveChangesAsync(ct);
    }

    public virtual async Task ClearStatusAsync(JobRef job, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateAsync(job.TenantId, ct);

        var row = await db.Set<ServiceJob>().FirstOrDefaultAsync(r => r.Id == job.Id, ct);
        if (row is null) return;

        row.LastStatus = null;
        row.LastStatusMessage = null;

        await db.SaveChangesAsync(ct);
    }

    public virtual async Task MarkRunStartedAsync(JobRunStarted run, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateAsync(run.Job.TenantId, ct);

        var row = await db.Set<ServiceJob>().FirstOrDefaultAsync(r => r.Id == run.Job.Id, ct);
        if (row is null) return;

        row.LastStatus = nameof(JobRunStatus.Running);
        row.LastStatusMessage = $"Started at {run.StartedUtc:u}";

        if (row.EnableHistory)
        {
            // Inserted with no StopDateTime on purpose. A host that dies mid-run leaves this row
            // open, which is the only evidence the run ever started.
            db.Set<ServiceJobHistory>().Add(new ServiceJobHistory
            {
                ServiceJobId = row.Id,
                StartDateTime = run.StartedUtc,
                StopDateTime = null,
                Status = nameof(JobRunStatus.Running),
                StatusMessage = row.LastStatusMessage,
                Attempt = run.Attempt,
                FireInstanceId = run.FireInstanceId,
                SchedulerInstanceId = run.SchedulerInstanceId,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public virtual async Task MarkRunCompletedAsync(JobRunCompleted run, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateAsync(run.Job.TenantId, ct);

        var row = await db.Set<ServiceJob>().FirstOrDefaultAsync(r => r.Id == run.Job.Id, ct);
        if (row is null) return;

        row.LastRunDateTime = run.CompletedUtc;
        row.LastRunDurationSeconds = (int)Math.Round(run.Duration.TotalSeconds);
        row.LastStatus = run.Status.ToString();
        row.LastStatusMessage = run.Message ?? string.Empty;

        // Only a genuine success advances this. A retry pending or a timeout must not.
        if (run.IsSuccess) row.LastSuccessfulRunDateTime = run.CompletedUtc;

        if (row.EnableHistory)
        {
            await CloseHistoryAsync(db, row, run, ct);
        }

        await db.SaveChangesAsync(ct);

        if (row.EnableHistory)
        {
            await TrimHistoryAsync(db, row, ct);
        }
    }

    /// <summary>
    /// Closes the open history row for this fire, or creates one if the start was never recorded
    /// (history enabled mid-run, or the start write failed).
    /// </summary>
    protected virtual async Task CloseHistoryAsync(TContext db, ServiceJob row, JobRunCompleted run, CancellationToken ct)
    {
        var history = await db.Set<ServiceJobHistory>()
            .Where(h => h.ServiceJobId == row.Id && h.FireInstanceId == run.FireInstanceId)
            .OrderByDescending(h => h.Id)
            .FirstOrDefaultAsync(ct);

        if (history is null)
        {
            history = new ServiceJobHistory
            {
                ServiceJobId = row.Id,
                StartDateTime = run.CompletedUtc - run.Duration,
                Attempt = run.Attempt,
                FireInstanceId = run.FireInstanceId,
            };
            db.Set<ServiceJobHistory>().Add(history);
        }

        history.StopDateTime = run.CompletedUtc;
        history.Status = run.Status.ToString();
        history.StatusMessage = run.Message ?? string.Empty;
        history.DurationMs = (int)Math.Round(run.Duration.TotalMilliseconds);
        history.ExceptionType = run.Exception?.GetType().FullName;
        history.StackTrace = run.Exception?.StackTrace;
    }

    /// <summary>
    /// Keeps at most <c>ServiceJob.HistoryCount</c> rows for a job, deleting the oldest.
    /// </summary>
    /// <remarks>
    /// Runs as a separate round trip after the completion is committed: trimming is housekeeping and
    /// must not be able to roll back the run record it follows.
    /// </remarks>
    protected virtual async Task TrimHistoryAsync(TContext db, ServiceJob row, CancellationToken ct)
    {
        if (row.HistoryCount <= 0) return;

        var excess = await db.Set<ServiceJobHistory>()
            .Where(h => h.ServiceJobId == row.Id)
            .OrderByDescending(h => h.Id)
            .Skip(row.HistoryCount)
            .ToListAsync(ct);

        if (excess.Count == 0) return;

        db.Set<ServiceJobHistory>().RemoveRange(excess);
        await db.SaveChangesAsync(ct);
    }
}

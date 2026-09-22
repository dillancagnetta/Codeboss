using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodeBoss.Jobs.EntityFrameworkCore.Tests;

public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : DbContext(options)
{
    public DbSet<ServiceJob> ServiceJobs => Set<ServiceJob>();
    public DbSet<ServiceJobHistory> ServiceJobHistory => Set<ServiceJobHistory>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ServiceJob>(job =>
        {
            job.HasKey(x => x.Id);
            job.Ignore(x => x.CronDescription);
            job.Ignore(x => x.LastRunStatus);
            job.Property(x => x.JobParameters)
                .HasConversion(
                    v => v == null ? null : string.Join(';', v.Select(kv => $"{kv.Key}={kv.Value}")),
                    v => string.IsNullOrEmpty(v)
                        ? new Dictionary<string, string>()
                        : v.Split(';', StringSplitOptions.RemoveEmptyEntries)
                           .Select(pair => pair.Split('=', 2))
                           .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : string.Empty));
            job.HasMany(x => x.ServiceJobHistory).WithOne(x => x.ServiceJob).HasForeignKey(x => x.ServiceJobId);
        });

        builder.Entity<ServiceJobHistory>(history => history.HasKey(x => x.Id));
    }
}

/// <summary>Creates a context over one shared in-memory SQLite database.</summary>
public sealed class TestContextFactory(SqliteConnection connection) : IServiceJobDbContextFactory<JobsDbContext>
{
    public int? LastTenantAsked { get; private set; }

    public ValueTask<JobsDbContext> CreateAsync(int? tenantId, CancellationToken ct = default)
    {
        LastTenantAsked = tenantId;

        var options = new DbContextOptionsBuilder<JobsDbContext>().UseSqlite(connection).Options;
        return ValueTask.FromResult(new JobsDbContext(options));
    }
}

public class EfServiceJobRepositoryTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TestContextFactory _factory = null!;
    private EfServiceJobRepository<JobsDbContext> _repository = null!;

    public async Task InitializeAsync()
    {
        // A real relational provider, not the in-memory fake: this exercises actual SQL translation
        // of the history queries.
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _factory = new TestContextFactory(_connection);
        _repository = new EfServiceJobRepository<JobsDbContext>(_factory);

        await using var db = await _factory.CreateAsync(null);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private async Task<ServiceJob> SeedAsync(bool enableHistory = false, int historyCount = 500, bool isActive = true)
    {
        await using var db = await _factory.CreateAsync(null);

        var job = new ServiceJob
        {
            JobKey = Guid.NewGuid(),
            Name = "Seeded",
            Class = "Some.Job",
            Assembly = "Some.Assembly",
            CronExpression = "0 0 6 * * ?",
            IsActive = isActive,
            EnableHistory = enableHistory,
            HistoryCount = historyCount,
        };

        db.ServiceJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private async Task<List<ServiceJobHistory>> HistoryAsync(int jobId)
    {
        await using var db = await _factory.CreateAsync(null);
        return await db.ServiceJobHistory.Where(h => h.ServiceJobId == jobId).OrderBy(h => h.Id).ToListAsync();
    }

    private async Task<ServiceJob> ReloadAsync(int id)
    {
        await using var db = await _factory.CreateAsync(null);
        return await db.ServiceJobs.AsNoTracking().FirstAsync(j => j.Id == id);
    }

    // ── Reads ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveJobs_ReturnsOnlyActiveOnes()
    {
        await SeedAsync(isActive: true);
        await SeedAsync(isActive: false);

        var active = await _repository.GetActiveJobsAsync(null);

        Assert.Single(active);
        Assert.True(active[0].IsActive);
    }

    [Fact]
    public async Task Find_MissingRow_ReturnsNullRatherThanThrowing()
    {
        // A job whose row was deleted mid-flight must not blow up the scheduler.
        Assert.Null(await _repository.FindAsync(new JobRef(9999, null)));
    }

    [Fact]
    public async Task Find_PassesTheTenantToTheContextFactory()
    {
        await _repository.FindAsync(new JobRef(1, 42));

        Assert.Equal(42, _factory.LastTenantAsked);
    }

    // ── Status ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetStatus_WritesTheEnumNameAndMessageSeparately()
    {
        var job = await SeedAsync();

        await _repository.SetStatusAsync(new JobRef(job.Id, null), JobRunStatus.SchedulingError, "a long explanation");

        var reloaded = await ReloadAsync(job.Id);
        Assert.Equal(nameof(JobRunStatus.SchedulingError), reloaded.LastStatus);
        Assert.Equal("a long explanation", reloaded.LastStatusMessage);
        Assert.Equal(JobRunStatus.SchedulingError, reloaded.LastRunStatus);
    }

    [Fact]
    public async Task ClearStatus_RemovesBoth()
    {
        var job = await SeedAsync();
        await _repository.SetStatusAsync(new JobRef(job.Id, null), JobRunStatus.Failed, "broke");

        await _repository.ClearStatusAsync(new JobRef(job.Id, null));

        var reloaded = await ReloadAsync(job.Id);
        Assert.Null(reloaded.LastStatus);
        Assert.Null(reloaded.LastStatusMessage);
    }

    [Fact]
    public async Task SetStatus_MissingRow_IsANoOp()
    {
        await _repository.SetStatusAsync(new JobRef(9999, null), JobRunStatus.Failed, "nobody home");
    }

    // ── Run tracking ─────────────────────────────────────────────────────────

    private static JobRunStarted Started(ServiceJob job, string fireId = "fire-1", int attempt = 1) =>
        new(new JobRef(job.Id, null), fireId, "node-1", attempt, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc));

    private static JobRunCompleted Completed(ServiceJob job, JobRunStatus status, string fireId = "fire-1",
        int attempt = 1, Exception exception = null) =>
        new(new JobRef(job.Id, null), fireId, attempt, status, status.ToString(), TimeSpan.FromSeconds(3),
            new DateTime(2026, 5, 1, 10, 0, 3, DateTimeKind.Utc), exception);

    [Fact]
    public async Task MarkRunCompleted_RecordsTimingsAndStatus()
    {
        var job = await SeedAsync();

        await _repository.MarkRunStartedAsync(Started(job));
        await _repository.MarkRunCompletedAsync(Completed(job, JobRunStatus.Succeeded));

        var reloaded = await ReloadAsync(job.Id);
        Assert.Equal(nameof(JobRunStatus.Succeeded), reloaded.LastStatus);
        Assert.Equal(3, reloaded.LastRunDurationSeconds);
        Assert.NotNull(reloaded.LastRunDateTime);
        Assert.Equal(reloaded.LastRunDateTime, reloaded.LastSuccessfulRunDateTime);
    }

    [Theory]
    [InlineData(JobRunStatus.Failed)]
    [InlineData(JobRunStatus.TimedOut)]
    [InlineData(JobRunStatus.Retrying)]
    public async Task MarkRunCompleted_NonSuccess_DoesNotAdvanceLastSuccessfulRun(JobRunStatus status)
    {
        var job = await SeedAsync();

        await _repository.MarkRunCompletedAsync(Completed(job, status));

        var reloaded = await ReloadAsync(job.Id);
        Assert.Equal(status.ToString(), reloaded.LastStatus);
        Assert.Null(reloaded.LastSuccessfulRunDateTime);
    }

    [Fact]
    public async Task HistoryDisabled_WritesNoRows()
    {
        var job = await SeedAsync(enableHistory: false);

        await _repository.MarkRunStartedAsync(Started(job));
        await _repository.MarkRunCompletedAsync(Completed(job, JobRunStatus.Succeeded));

        Assert.Empty(await HistoryAsync(job.Id));
    }

    [Fact]
    public async Task HistoryEnabled_StartOpensARowAndCompletionClosesTheSameOne()
    {
        var job = await SeedAsync(enableHistory: true);

        await _repository.MarkRunStartedAsync(Started(job));

        var open = Assert.Single(await HistoryAsync(job.Id));
        Assert.Null(open.StopDateTime); // an interrupted host leaves this visible
        Assert.Equal("fire-1", open.FireInstanceId);
        Assert.Equal("node-1", open.SchedulerInstanceId);

        await _repository.MarkRunCompletedAsync(Completed(job, JobRunStatus.Succeeded));

        // One row per run, not two.
        var closed = Assert.Single(await HistoryAsync(job.Id));
        Assert.Equal(open.Id, closed.Id);
        Assert.NotNull(closed.StopDateTime);
        Assert.Equal(3000, closed.DurationMs);
        Assert.Equal(nameof(JobRunStatus.Succeeded), closed.Status);
    }

    [Fact]
    public async Task HistoryEnabled_FailureRecordsTheExceptionDetail()
    {
        var job = await SeedAsync(enableHistory: true);
        Exception thrown;
        try { throw new InvalidOperationException("it broke"); } catch (Exception ex) { thrown = ex; }

        await _repository.MarkRunStartedAsync(Started(job));
        await _repository.MarkRunCompletedAsync(Completed(job, JobRunStatus.Failed, exception: thrown));

        var row = Assert.Single(await HistoryAsync(job.Id));
        Assert.Equal(typeof(InvalidOperationException).FullName, row.ExceptionType);
        Assert.NotNull(row.StackTrace);
    }

    [Fact]
    public async Task HistoryEnabled_CompletionWithNoStart_StillRecordsARow()
    {
        // History switched on between the start and the finish, or the start write failed.
        var job = await SeedAsync(enableHistory: true);

        await _repository.MarkRunCompletedAsync(Completed(job, JobRunStatus.Succeeded));

        var row = Assert.Single(await HistoryAsync(job.Id));
        Assert.NotNull(row.StopDateTime);
        Assert.Equal(nameof(JobRunStatus.Succeeded), row.Status);
    }

    [Fact]
    public async Task RetriesOfOneFire_EachGetTheirOwnHistoryRow()
    {
        var job = await SeedAsync(enableHistory: true);

        await _repository.MarkRunStartedAsync(Started(job, "fire-1", attempt: 1));
        await _repository.MarkRunCompletedAsync(Completed(job, JobRunStatus.Retrying, "fire-1", attempt: 1));
        await _repository.MarkRunStartedAsync(Started(job, "fire-2", attempt: 2));
        await _repository.MarkRunCompletedAsync(Completed(job, JobRunStatus.Succeeded, "fire-2", attempt: 2));

        var rows = await HistoryAsync(job.Id);
        Assert.Equal(2, rows.Count);
        Assert.Equal([1, 2], rows.Select(r => r.Attempt));
        Assert.Equal([nameof(JobRunStatus.Retrying), nameof(JobRunStatus.Succeeded)], rows.Select(r => r.Status));
    }

    [Fact]
    public async Task History_IsTrimmedToHistoryCount()
    {
        var job = await SeedAsync(enableHistory: true, historyCount: 3);

        for (var i = 1; i <= 7; i++)
        {
            await _repository.MarkRunStartedAsync(Started(job, $"fire-{i}"));
            await _repository.MarkRunCompletedAsync(Completed(job, JobRunStatus.Succeeded, $"fire-{i}"));
        }

        var rows = await HistoryAsync(job.Id);
        Assert.Equal(3, rows.Count);
        // The newest survive.
        Assert.Equal(["fire-5", "fire-6", "fire-7"], rows.Select(r => r.FireInstanceId));
    }

    [Fact]
    public async Task History_ZeroHistoryCount_DisablesTrimming()
    {
        var job = await SeedAsync(enableHistory: true, historyCount: 0);

        for (var i = 1; i <= 3; i++)
        {
            await _repository.MarkRunStartedAsync(Started(job, $"fire-{i}"));
            await _repository.MarkRunCompletedAsync(Completed(job, JobRunStatus.Succeeded, $"fire-{i}"));
        }

        Assert.Equal(3, (await HistoryAsync(job.Id)).Count);
    }
}

using System.Collections.Concurrent;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.EntityFrameworkCore;
using CodeBoss.Jobs.Jobs;
using CodeBoss.Jobs.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;

namespace CodeBoss.Jobs.IntegrationTests;

/// <summary>
/// The library's riskiest behaviour is exactly what a unit test cannot reach: a clustered ADO job
/// store, <c>useProperties</c> serialisation of the job data map, and whether two scheduler nodes
/// sharing one store fire each trigger once or twice. These run against a real PostgreSQL container.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ClusteredSchedulerTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly List<ServiceProvider> _nodes = [];
    private readonly List<IScheduler> _schedulers = [];

    public async Task InitializeAsync() => await fixture.ResetAsync();

    /// <summary>
    /// Stops every scheduler this test started, WAITING for it to finish, then disposes the nodes.
    ///
    /// <para>This matters more than it looks. <c>Shutdown(waitForJobsToComplete: false)</c> returns
    /// before Quartz's threads have stopped, so a node from a finished test kept firing into the next
    /// test's freshly truncated tables. That surfaced two ways: history rows whose matching start row
    /// had been truncated away, and a Postgres deadlock between TRUNCATE and a live scheduler's row
    /// locks.</para>
    /// </summary>
    public async Task DisposeAsync()
    {
        foreach (var scheduler in _schedulers)
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        foreach (var node in _nodes)
        {
            await node.DisposeAsync();
        }
    }

    // ── Observation sink, shared across "nodes" in-process ───────────────────

    public sealed class Sink
    {
        public ConcurrentQueue<string> Fires { get; } = new();
        public ConcurrentQueue<string> Parameters { get; } = new();
        public TaskCompletionSource Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FailUntilAttempt { get; set; }
    }

    [JobDefinitionId("integration.probe")]
    public class ProbeJob(IServiceJobRepository repository, Sink sink, ILogger<ProbeJob> logger)
        : CodeBossJob(repository, logger)
    {
        public override Task Execute(CancellationToken ct = default)
        {
            sink.Fires.Enqueue(Guid.NewGuid().ToString());
            if (Parameters.TryGetValue("Mode", out var mode)) sink.Parameters.Enqueue(mode);

            if (sink.Fires.Count < sink.FailUntilAttempt) throw new InvalidOperationException("not yet");

            sink.Ran.TrySetResult();
            return Task.CompletedTask;
        }
    }

    // ── Host wiring ──────────────────────────────────────────────────────────

    private ServiceProvider BuildNode(Sink sink, Action<CodeBossJobsOptions> configure = null)
    {
        var services = new ServiceCollection();

        // Quartz installs a PROCESS-GLOBAL logging hook that captures whichever ILoggerFactory it
        // first sees. A per-node factory would be disposed with that node, and every later test in
        // the process would then fail on a disposed LoggerFactory. NullLoggerFactory's Dispose is a
        // no-op, so sharing one instance is safe.
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IHostApplicationLifetime, StubHostLifetime>();
        services.AddSingleton(sink);
        services.AddSingleton(fixture);
        services.AddScoped<IServiceJobDbContextFactory<JobsDbContext>, FixtureDbContextFactory>();
        services.AddCodeBossJob<ProbeJob>();

        services.AddCodeBossJobs(opt =>
        {
            opt.Repo = typeof(EfServiceJobRepository<JobsDbContext>);
            opt.PulseInterval = TimeSpan.FromSeconds(2);
            opt.ConfigureQuartz = q =>
            {
                q.SchedulerName = "TestScheduler";
                q.SchedulerId = "AUTO";
                q.UsePersistentStore(store =>
                {
                    store.UseClustering(c => c.CheckinInterval = TimeSpan.FromSeconds(2));
                    store.UsePostgres(ado => ado.ConnectionString = fixture.ConnectionString);
                    // The library writes string-only job data precisely so this mode is safe.
                    store.UseProperties = true;
                    store.UseSystemTextJsonSerializer();
                    store.PerformSchemaValidation = true;
                });
            };
            configure?.Invoke(opt);
        });

        // ValidateOnBuild proves every registered job's dependencies resolve at startup rather than
        // at its first fire in production.
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        _nodes.Add(provider);
        return provider;
    }

    private async Task<ServiceJob> SeedJobAsync(string cron = "0/1 * * * * ?", Action<ServiceJob> configure = null)
    {
        await using var db = fixture.CreateDbContext();

        var job = new ServiceJob
        {
            JobKey = Guid.NewGuid(),
            Name = "Probe",
            // Stored by STABLE ID with no assembly — only the registry can resolve this.
            Class = "integration.probe",
            Assembly = null,
            CronExpression = cron,
            IsActive = true,
            EnableHistory = true,
            HistoryCount = 50,
        };
        configure?.Invoke(job);

        db.ServiceJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private async Task<IScheduler> StartAsync(ServiceProvider provider)
    {
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        _schedulers.Add(scheduler);
        return scheduler;
    }

    private static async Task<bool> WaitAsync(Task task, int seconds = 45)
        => ReferenceEquals(await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))), task);

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, int seconds = 45)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(250);
        }

        return false;
    }

    private Task<long> ScheduledJobCountAsync() =>
        fixture.ScalarAsync("SELECT count(*) FROM qrtz_job_details WHERE job_group <> 'System'");

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PulseSchedulesAJobAndItFiresAgainstAPersistentStore()
    {
        await SeedJobAsync();
        var sink = new Sink();
        await StartAsync(BuildNode(sink));

        Assert.True(await WaitAsync(sink.Ran.Task), "The job never ran.");
    }

    /// <summary>
    /// The guarantee ChurchManagerApi's deployment rests on: two processes share one clustered store,
    /// and each scheduled fire executes once across the cluster, not once per node.
    /// </summary>
    [Fact]
    public async Task TwoNodesSharingAStore_FireEachTriggerExactlyOnce()
    {
        await SeedJobAsync("0/2 * * * * ?");

        var sink = new Sink();
        await StartAsync(BuildNode(sink));
        await StartAsync(BuildNode(sink));

        Assert.True(await WaitAsync(sink.Ran.Task), "The job never ran on either node.");

        await Task.Delay(TimeSpan.FromSeconds(9));

        await using var db = fixture.CreateDbContext();
        var history = await db.ServiceJobHistory.CountAsync(h => h.StopDateTime != null);
        var fires = sink.Fires.Count;

        // One completed history row per execution: if both nodes ran the same fire, these diverge.
        Assert.Equal(fires, history);

        // Roughly 4 fires in 9 seconds on a 2-second cron. Double-firing would roughly double it.
        Assert.InRange(fires, 2, 7);
    }

    [Fact]
    public async Task SchedulerStateSurvivesARestart()
    {
        await SeedJobAsync("0 0 6 * * ?"); // far enough off that nothing fires during the test

        var scheduler = await StartAsync(BuildNode(new Sink()));
        Assert.True(await WaitUntilAsync(async () => await ScheduledJobCountAsync() == 1));
        await scheduler.Shutdown(waitForJobsToComplete: true);

        // The store holds the schedule, not the process.
        Assert.Equal(1, await ScheduledJobCountAsync());
    }

    [Fact]
    public async Task JobDataRoundTripsThroughUseProperties()
    {
        // useProperties = true rejects any non-string job data value. The library coerces everything,
        // including the tenant id, which is an int at the call site.
        await SeedJobAsync(configure: job => job.JobParameters = new Dictionary<string, string> { ["Mode"] = "backfill" });

        var sink = new Sink();
        await StartAsync(BuildNode(sink));

        Assert.True(await WaitAsync(sink.Ran.Task), "The job never ran.");
        Assert.True(sink.Parameters.TryDequeue(out var mode));
        Assert.Equal("backfill", mode);
    }

    [Fact]
    public async Task AJobStoredByStableIdWithNoAssembly_Resolves()
    {
        // Reflection cannot resolve a row whose assembly is null; only the registry can.
        await SeedJobAsync();

        var sink = new Sink();
        await StartAsync(BuildNode(sink));

        Assert.True(await WaitAsync(sink.Ran.Task), "A job stored by stable id did not resolve.");
    }

    [Fact]
    public async Task UnresolvableJobType_RecordsASchedulingErrorOnTheRow()
    {
        var job = await SeedJobAsync(configure: j =>
        {
            j.Class = "no.such.job";
            j.Assembly = "NoSuchAssembly";
        });

        await StartAsync(BuildNode(new Sink()));

        Assert.True(await WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            var row = await db.ServiceJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
            return row.LastRunStatus == JobRunStatus.SchedulingError;
        }), "A job with an unloadable type was skipped silently.");

        await using var check = fixture.CreateDbContext();
        var reloaded = await check.ServiceJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        Assert.Contains("no.such.job", reloaded.LastStatusMessage);
        // LastStatus is MaxLength(50); Postgres rejects an overflow outright.
        Assert.True(reloaded.LastStatus!.Length <= 50);
    }

    [Fact]
    public async Task RunHistoryIsWrittenToPostgres()
    {
        var job = await SeedJobAsync();

        var sink = new Sink();
        await StartAsync(BuildNode(sink));

        Assert.True(await WaitAsync(sink.Ran.Task), "The job never ran.");
        Assert.True(await WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            return await db.ServiceJobHistory.AnyAsync(h => h.StopDateTime != null);
        }), "No completed history row was written.");

        await using var check = fixture.CreateDbContext();
        var row = await check.ServiceJobHistory.AsNoTracking()
            .Where(h => h.StopDateTime != null).OrderBy(h => h.Id).FirstAsync();

        Assert.Equal(job.Id, row.ServiceJobId);
        Assert.Equal(nameof(JobRunStatus.Succeeded), row.Status);
        Assert.False(string.IsNullOrEmpty(row.FireInstanceId));
        Assert.False(string.IsNullOrEmpty(row.SchedulerInstanceId));

        var reloaded = await check.ServiceJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        Assert.NotNull(reloaded.LastSuccessfulRunDateTime);
    }

    [Fact]
    public async Task FailingJob_RetriesViaAPersistedTrigger()
    {
        var seeded = await SeedJobAsync("0 0 6 * * ?", job =>
        {
            job.MaxRetries = 2;
            job.RetryBackoffBaseSeconds = 1;
            job.RetryBackoffMaxSeconds = 1;
        });

        var sink = new Sink { FailUntilAttempt = 3 }; // fails twice, succeeds on the third
        var node = BuildNode(sink);
        await StartAsync(node);

        using var scope = node.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<IJobCommands>();

        await commands.RequestSyncAsync();
        Assert.True(await WaitUntilAsync(async () => await ScheduledJobCountAsync() == 1));

        // The cron is hours away, so every execution after this one can only come from a persisted
        // retry trigger.
        await commands.RunNowAsync(new JobRef(seeded.Id, null));

        Assert.True(await WaitAsync(sink.Ran.Task), "The job never succeeded after its retries.");
        Assert.Equal(3, sink.Fires.Count);

        Assert.True(await WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            var row = await db.ServiceJobs.AsNoTracking().FirstAsync();
            return row.LastRunStatus == JobRunStatus.Succeeded;
        }), "The final status was not Succeeded.");

        await using var final = fixture.CreateDbContext();
        var statuses = await final.ServiceJobHistory.AsNoTracking()
            .Where(h => h.StopDateTime != null).OrderBy(h => h.Id).Select(h => h.Status).ToListAsync();

        Assert.Equal(nameof(JobRunStatus.Retrying), statuses[0]);
        Assert.Equal(nameof(JobRunStatus.Succeeded), statuses[^1]);
    }

    [Fact]
    public async Task RequestSync_SchedulesANewJobWithoutWaitingForTheInterval()
    {
        var node = BuildNode(new Sink(), o =>
        {
            o.PulseInterval = TimeSpan.FromHours(12);
            o.PulseOnStartup = false;
        });
        await StartAsync(node);

        await SeedJobAsync("0 0 6 * * ?"); // added AFTER the scheduler started
        Assert.Equal(0, await ScheduledJobCountAsync());

        using var scope = node.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IJobCommands>().RequestSyncAsync();

        Assert.True(await WaitUntilAsync(async () => await ScheduledJobCountAsync() == 1),
            "The job was not scheduled after an explicit sync request.");
    }

    [Fact]
    public async Task DeactivatedJob_IsUnscheduledOnTheNextPulse()
    {
        var job = await SeedJobAsync("0 0 6 * * ?");

        await StartAsync(BuildNode(new Sink()));
        Assert.True(await WaitUntilAsync(async () => await ScheduledJobCountAsync() == 1));

        await using (var db = fixture.CreateDbContext())
        {
            var row = await db.ServiceJobs.FirstAsync(j => j.Id == job.Id);
            row.IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.True(await WaitUntilAsync(async () => await ScheduledJobCountAsync() == 0),
            "A deactivated job was left in the scheduler.");
    }
}

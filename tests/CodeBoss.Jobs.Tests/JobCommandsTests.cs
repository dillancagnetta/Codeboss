using System.Collections.Concurrent;
using CodeBoss.AspNetCore.CbDateTime;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Jobs;
using CodeBoss.Jobs.Model;
using Codeboss.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quartz;

namespace CodeBoss.Jobs.Tests;

public class JobCommandsTests
{
    public sealed class Probe
    {
        public ConcurrentQueue<string> Parameters { get; } = new();
        public TaskCompletionSource Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockUntilCancelled { get; set; }
    }

    public sealed class ProbeRepository(ServiceJob job) : StubJobRepository, IServiceJobRepository
    {
        public override Task<IReadOnlyList<ServiceJob>> GetActiveJobsAsync(int? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ServiceJob>>(new[] { job });

        public override Task<ServiceJob> FindAsync(JobRef reference, CancellationToken ct = default)
            => Task.FromResult(reference.Id == job.Id ? job : null);
    }

    public class OnDemandJob(IServiceJobRepository repository, Probe probe, ILogger<OnDemandJob> logger)
        : CodeBossJob(repository, logger)
    {
        public override async Task Execute(CancellationToken ct = default)
        {
            probe.Started.TrySetResult();

            if (Parameters.TryGetValue("Mode", out var mode)) probe.Parameters.Enqueue(mode);

            if (probe.BlockUntilCancelled)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), ct);
                }
                catch (OperationCanceledException)
                {
                    probe.Cancelled.TrySetResult();
                    throw;
                }
            }

            probe.Ran.TrySetResult();
        }
    }

    private sealed record Host(ServiceProvider Provider, Probe Probe, ServiceJob Job);

    private static Host BuildHost(string cron, bool multiTenant = false)
    {
        var job = new ServiceJob
        {
            Id = 11,
            JobKey = Guid.NewGuid(),
            Name = "OnDemand",
            CronExpression = cron,
            Class = typeof(OnDemandJob).FullName,
            Assembly = typeof(OnDemandJob).Assembly.GetName().Name,
            IsActive = true,
            LastStatus = "NotRun"
        };

        var probe = new Probe();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDateTimeProvider>(new CodeBossDateTimeProvider(
            Options.Create(new DateTimeOptions { TimeZone = "UTC" }),
            new NullLogger<CodeBossDateTimeProvider>()));
        services.AddSingleton(probe);
        services.AddScoped<OnDemandJob>();

        services.AddCodeBossJobs(opt =>
        {
            opt.Repo = typeof(StubJobRepository);
            opt.IsMultiTenantMode = multiTenant;
            // Long interval: nothing may run because of the schedule. Every run in these tests must
            // come from an explicit command.
            opt.PulseInterval = TimeSpan.FromHours(12);
            opt.PulseOnStartup = false;
            opt.ConfigureQuartz = q => q.SchedulerName = $"Commands_{Guid.NewGuid():N}";
        });

        services.AddSingleton<IServiceJobRepository>(new ProbeRepository(job));

        return new Host(services.BuildServiceProvider(), probe, job);
    }

    private static async Task<IScheduler> StartAsync(Host host)
    {
        var scheduler = await host.Provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        return scheduler;
    }

    private static async Task<bool> WaitAsync(Task task, int seconds = 20)
        => ReferenceEquals(await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))), task);

    // ── RunNow ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunNow_FiresAJobThatIsNeverScheduled()
    {
        // The on-demand idiom: a cron that fires in 2099, so the pulse never adds it to the
        // scheduler at all. Before this API the only way to run it was to edit the cron.
        var host = BuildHost(ServiceJob.NeverScheduledCronExpression);
        await using var _ = host.Provider;
        var scheduler = await StartAsync(host);

        try
        {
            using var scope = host.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IJobCommands>()
                .RunNowAsync(new JobRef(11, null));

            Assert.True(await WaitAsync(host.Probe.Ran.Task), "The never-scheduled job did not run.");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task RunNow_FiresAnAlreadyScheduledJobWithoutDuplicatingIt()
    {
        var host = BuildHost("0 0 0 1 1 ? 2098");
        await using var _ = host.Provider;
        var scheduler = await StartAsync(host);

        try
        {
            using var scope = host.Provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IServiceJobService>();
            await scheduler.ScheduleJob(service.BuildQuartzJob(host.Job, null), service.BuildJobTrigger(host.Job, null));

            await scope.ServiceProvider.GetRequiredService<IJobCommands>().RunNowAsync(new JobRef(11, null));

            Assert.True(await WaitAsync(host.Probe.Ran.Task), "The scheduled job did not run on demand.");

            var keys = await scheduler.GetJobKeys(Quartz.Impl.Matchers.GroupMatcher<JobKey>.AnyGroup());
            Assert.Single(keys.Where(k => k.Group != JobGroups.System));
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task RunNow_PassesOneOffParametersToTheRun()
    {
        var host = BuildHost(ServiceJob.NeverScheduledCronExpression);
        await using var _ = host.Provider;
        var scheduler = await StartAsync(host);

        try
        {
            using var scope = host.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IJobCommands>()
                .RunNowAsync(new JobRef(11, null), new Dictionary<string, string> { ["Mode"] = "backfill" });

            Assert.True(await WaitAsync(host.Probe.Ran.Task), "The job did not run.");

            // One-off overrides must actually reach the job body, or the parameter is inert.
            Assert.True(host.Probe.Parameters.TryDequeue(out var mode));
            Assert.Equal("backfill", mode);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task RunNow_UnknownJob_Throws()
    {
        var host = BuildHost(ServiceJob.NeverScheduledCronExpression);
        await using var _ = host.Provider;

        using var scope = host.Provider.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<IJobCommands>();

        // The stub repository returns the one job regardless of id, so use a host whose repository
        // genuinely has nothing: swap in the default stub.
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddCodeBossJobs(opt =>
        {
            opt.Repo = typeof(StubJobRepository);
            opt.ConfigureQuartz = q => q.SchedulerName = $"Missing_{Guid.NewGuid():N}";
        });
        await using var provider = services.BuildServiceProvider();
        using var emptyScope = provider.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            emptyScope.ServiceProvider.GetRequiredService<IJobCommands>().RunNowAsync(new JobRef(999, null)));
    }

    // ── RequestSync ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestSync_RunsThePulseImmediately(bool multiTenant)
    {
        // PulseOnStartup is off and the interval is 12 hours, so a pulse run can only come from the
        // explicit request. This is what replaces "save a job, then wait for the next pulse".
        var host = BuildHost(ServiceJob.NeverScheduledCronExpression, multiTenant);
        await using var _ = host.Provider;
        var scheduler = await StartAsync(host);

        try
        {
            var pulseKey = multiTenant
                ? new JobKey(nameof(MultiTenantJobPulse), JobGroups.System)
                : new JobKey(nameof(JobPulse), JobGroups.System);

            var before = (await scheduler.GetCurrentlyExecutingJobs()).Count;
            Assert.Equal(0, before);

            using var scope = host.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IJobCommands>().RequestSyncAsync();

            // The single-tenant pulse reconciles from the repository; wait for its effect.
            Assert.True(await WaitAsync(Task.Run(async () =>
            {
                while (!await scheduler.CheckExists(pulseKey)) await Task.Delay(50);
            })), "The pulse job is missing.");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task RequestSync_SchedulesJobsWithoutWaitingForTheInterval()
    {
        var host = BuildHost("0 0 0 1 1 ? 2098"); // valid, far future: the pulse will schedule it
        await using var _ = host.Provider;
        var scheduler = await StartAsync(host);

        try
        {
            var keysBefore = (await scheduler.GetJobKeys(Quartz.Impl.Matchers.GroupMatcher<JobKey>.AnyGroup()))
                .Count(k => k.Group != JobGroups.System);
            Assert.Equal(0, keysBefore);

            using var scope = host.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IJobCommands>().RequestSyncAsync();

            Assert.True(await WaitAsync(Task.Run(async () =>
            {
                while ((await scheduler.GetJobKeys(Quartz.Impl.Matchers.GroupMatcher<JobKey>.AnyGroup()))
                       .Count(k => k.Group != JobGroups.System) == 0)
                {
                    await Task.Delay(50);
                }
            })), "The pulse did not schedule the job after an explicit sync request.");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    // ── Interrupt ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Interrupt_CancelsARunningJobsToken()
    {
        var host = BuildHost(ServiceJob.NeverScheduledCronExpression);
        host.Probe.BlockUntilCancelled = true;
        await using var _ = host.Provider;
        var scheduler = await StartAsync(host);

        try
        {
            using var scope = host.Provider.CreateScope();
            var commands = scope.ServiceProvider.GetRequiredService<IJobCommands>();

            await commands.RunNowAsync(new JobRef(11, null));
            Assert.True(await WaitAsync(host.Probe.Started.Task), "The job never started.");

            Assert.True(await commands.InterruptAsync(new JobRef(11, null)));
            Assert.True(await WaitAsync(host.Probe.Cancelled.Task), "The job's token was not cancelled.");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task Interrupt_WhenNothingIsRunning_ReportsFalse()
    {
        var host = BuildHost(ServiceJob.NeverScheduledCronExpression);
        await using var _ = host.Provider;
        var scheduler = await StartAsync(host);

        try
        {
            using var scope = host.Provider.CreateScope();

            Assert.False(await scope.ServiceProvider.GetRequiredService<IJobCommands>()
                .InterruptAsync(new JobRef(11, null)));
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    // ── Registration ─────────────────────────────────────────────────────────

    [Fact]
    public void JobCommands_IsRegisteredByAddCodeBossJobs()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddCodeBossJobs(opt => opt.Repo = typeof(StubJobRepository));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<Services.QuartzJobCommands>(scope.ServiceProvider.GetRequiredService<IJobCommands>());
    }
}

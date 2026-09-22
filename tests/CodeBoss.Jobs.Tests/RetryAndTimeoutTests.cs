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

public class RetryAndTimeoutTests
{
    // ── RetryPolicy (pure) ───────────────────────────────────────────────────

    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }

    [Theory]
    [InlineData(1, 30)]    // base
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    public void Delay_GrowsExponentiallyFromTheBase(int failedAttempt, double expectedSeconds)
    {
        // Full jitter at its maximum gives the un-jittered exponential term.
        var delay = RetryPolicy.Delay(failedAttempt, TimeSpan.FromSeconds(30), TimeSpan.FromHours(24),
            new FixedRandom(1.0));

        Assert.Equal(expectedSeconds, delay.TotalSeconds, precision: 3);
    }

    [Fact]
    public void Delay_FirstRetryWaitsAFullBaseDelay_NotHalfOfOne()
    {
        // Guards the off-by-one that makes the exponent -1 on the first retry.
        var delay = RetryPolicy.Delay(1, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1), new FixedRandom(1.0));

        Assert.Equal(30, delay.TotalSeconds, precision: 3);
    }

    [Fact]
    public void Delay_IsCapped()
    {
        var delay = RetryPolicy.Delay(20, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15), new FixedRandom(1.0));

        Assert.Equal(TimeSpan.FromMinutes(15), delay);
    }

    [Fact]
    public void Delay_LargeAttemptCount_DoesNotOverflowToInfinity()
    {
        var delay = RetryPolicy.Delay(int.MaxValue, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15),
            new FixedRandom(1.0));

        Assert.Equal(TimeSpan.FromMinutes(15), delay);
    }

    [Fact]
    public void Delay_IsJittered()
    {
        // Jobs that fail together usually failed for the same reason. Retrying in lockstep rebuilds
        // the herd that caused it.
        var delays = Enumerable.Range(0, 200)
            .Select(_ => RetryPolicy.Delay(3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15)).TotalSeconds)
            .ToList();

        Assert.True(delays.Distinct().Count() > 100, "Delays are not spread out.");
        Assert.All(delays, d => Assert.InRange(d, 0, 120));
    }

    [Theory]
    [InlineData(0, 1, false)]  // no budget
    [InlineData(2, 1, true)]   // attempt 1 failed, 2 retries allowed
    [InlineData(2, 2, true)]
    [InlineData(2, 3, false)]  // exhausted
    public void ShouldRetry_RespectsTheBudget(int maxRetries, int failedAttempt, bool expected)
    {
        var job = new ServiceJob { MaxRetries = maxRetries };

        Assert.Equal(expected, RetryPolicy.ShouldRetry(job, failedAttempt));
    }

    [Fact]
    public void ShouldRetry_NullJob_IsFalse() => Assert.False(RetryPolicy.ShouldRetry(null, 1));

    // ── End-to-end ───────────────────────────────────────────────────────────

    public sealed class Tracker
    {
        public ConcurrentQueue<int> Attempts { get; } = new();
        public ConcurrentQueue<JobRunCompleted> Completed { get; } = new();
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FailUntilAttempt { get; set; } = int.MaxValue;
    }

    public sealed class TrackingRepository(ServiceJob job, Tracker tracker) : StubJobRepository, IServiceJobRepository
    {
        public override Task<IReadOnlyList<ServiceJob>> GetActiveJobsAsync(int? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ServiceJob>>(new[] { job });

        // One method, one tenant-optional reference. Previously this needed BOTH GetByIdAsync
        // overloads, and implementing only the plain one silently disabled every per-job setting.
        public override Task<ServiceJob> FindAsync(JobRef reference, CancellationToken ct = default)
            => Task.FromResult(job);

        public Task MarkRunCompletedAsync(JobRunCompleted run, CancellationToken ct = default)
        {
            tracker.Completed.Enqueue(run);
            if (run.Status is not JobRunStatus.Retrying) tracker.Finished.TrySetResult();
            return Task.CompletedTask;
        }
    }

    public class FlakyJob(IServiceJobRepository repository, Tracker tracker, ILogger<FlakyJob> logger)
        : CodeBossJob(repository, logger)
    {
        public override Task Execute(CancellationToken ct = default)
        {
            var attempt = tracker.Attempts.Count + 1;
            tracker.Attempts.Enqueue(attempt);

            if (attempt < tracker.FailUntilAttempt) throw new InvalidOperationException($"attempt {attempt} failed");

            Result = $"succeeded on attempt {attempt}";
            return Task.CompletedTask;
        }
    }

    public class SlowJob(IServiceJobRepository repository, Tracker tracker, ILogger<SlowJob> logger)
        : CodeBossJob(repository, logger)
    {
        public override async Task Execute(CancellationToken ct = default)
        {
            tracker.Attempts.Enqueue(1);
            await Task.Delay(TimeSpan.FromSeconds(30), ct); // observes the token
        }
    }

    public class UncooperativeJob(IServiceJobRepository repository, Tracker tracker, ILogger<UncooperativeJob> logger)
        : CodeBossJob(repository, logger)
    {
        public override async Task Execute(CancellationToken ct = default)
        {
            tracker.Attempts.Enqueue(1);
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None); // ignores the token
            Result = "finished anyway";
        }
    }

    private sealed record Host(ServiceProvider Provider, Tracker Tracker);

    private static Host BuildHost(Type jobType, Action<ServiceJob> configureJob = null)
    {
        var job = new ServiceJob
        {
            Id = 7,
            JobKey = Guid.NewGuid(),
            Name = "RetryProbe",
            // Far-future cron: every fire after the first must come from a retry trigger, never
            // from the schedule, or the test would not be measuring retries at all.
            CronExpression = "0 0 0 1 1 ? 2099",
            Class = jobType.FullName,
            Assembly = jobType.Assembly.GetName().Name,
            IsActive = true,
            LastStatus = "NotRun"
        };
        configureJob?.Invoke(job);

        var tracker = new Tracker();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDateTimeProvider>(new CodeBossDateTimeProvider(
            Options.Create(new DateTimeOptions { TimeZone = "UTC" }),
            new NullLogger<CodeBossDateTimeProvider>()));
        services.AddSingleton(tracker);
        services.AddScoped(jobType);

        services.AddCodeBossJobs(opt =>
        {
            opt.Repo = typeof(StubJobRepository);
            opt.PulseInterval = TimeSpan.FromHours(1);
            opt.PulseOnStartup = false;
            opt.ConfigureQuartz = q => q.SchedulerName = $"Retry_{Guid.NewGuid():N}";
        });

        services.AddSingleton<IServiceJobRepository>(new TrackingRepository(job, tracker));

        return new Host(services.BuildServiceProvider(), tracker);
    }

    private static async Task<IScheduler> StartAndFireAsync(Host host)
    {
        var scheduler = await host.Provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        var repository = (TrackingRepository)host.Provider.GetRequiredService<IServiceJobRepository>();
        var job = (await repository.GetActiveJobsAsync(null)).Single();
        var service = new Services.ServiceJobQuartzService(null);

        await scheduler.ScheduleJob(service.BuildQuartzJob(job), service.BuildQuartzTrigger(job));
        await scheduler.TriggerJob(service.BuildQuartzJob(job).Key);

        return scheduler;
    }

    private static async Task<bool> WaitAsync(Task task, int seconds = 30)
        => ReferenceEquals(await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))), task);

    [Fact]
    public async Task FailingJob_WithRetryBudget_IsRetriedUntilItSucceeds()
    {
        var host = BuildHost(typeof(FlakyJob), j =>
        {
            j.MaxRetries = 3;
            j.RetryBackoffBaseSeconds = 1;
            j.RetryBackoffMaxSeconds = 1;
            j.DisallowConcurrentExecution = true;
        });
        host.Tracker.FailUntilAttempt = 3; // fails on 1 and 2, succeeds on 3

        await using var _ = host.Provider;
        var scheduler = await StartAndFireAsync(host);

        try
        {
            Assert.True(await WaitAsync(host.Tracker.Finished.Task), "The job never reached a final status.");

            Assert.Equal(3, host.Tracker.Attempts.Count);

            var statuses = host.Tracker.Completed.Select(c => c.Status).ToList();
            Assert.Equal(JobRunStatus.Retrying, statuses[0]);
            Assert.Equal(JobRunStatus.Retrying, statuses[1]);
            Assert.Equal(JobRunStatus.Succeeded, statuses[^1]);

            // The retry trigger carries the attempt counter forward.
            Assert.Equal(new[] { 1, 2, 3 }, host.Tracker.Completed.Select(c => c.Attempt).ToArray());
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task FailingJob_WithNoRetryBudget_FailsImmediately()
    {
        var host = BuildHost(typeof(FlakyJob), j => j.MaxRetries = 0);
        host.Tracker.FailUntilAttempt = int.MaxValue;

        await using var _ = host.Provider;
        var scheduler = await StartAndFireAsync(host);

        try
        {
            Assert.True(await WaitAsync(host.Tracker.Finished.Task), "The job never reached a final status.");

            var completed = Assert.Single(host.Tracker.Completed);
            Assert.Equal(JobRunStatus.Failed, completed.Status);
            Assert.Single(host.Tracker.Attempts);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task ExhaustedRetries_EndInFailedNotRetrying()
    {
        var host = BuildHost(typeof(FlakyJob), j =>
        {
            j.MaxRetries = 1;
            j.RetryBackoffBaseSeconds = 1;
            j.RetryBackoffMaxSeconds = 1;
        });
        host.Tracker.FailUntilAttempt = int.MaxValue; // always fails

        await using var _ = host.Provider;
        var scheduler = await StartAndFireAsync(host);

        try
        {
            Assert.True(await WaitAsync(host.Tracker.Finished.Task), "The job never reached a final status.");

            Assert.Equal(2, host.Tracker.Attempts.Count); // original + 1 retry
            var statuses = host.Tracker.Completed.Select(c => c.Status).ToList();
            Assert.Equal(JobRunStatus.Retrying, statuses[0]);
            Assert.Equal(JobRunStatus.Failed, statuses[^1]);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task CooperativeJob_ExceedingItsTimeout_IsRecordedAsTimedOut()
    {
        var host = BuildHost(typeof(SlowJob), j => j.TimeoutSeconds = 1);

        await using var _ = host.Provider;
        var scheduler = await StartAndFireAsync(host);

        try
        {
            Assert.True(await WaitAsync(host.Tracker.Finished.Task), "The job never reached a final status.");

            var completed = Assert.Single(host.Tracker.Completed);
            Assert.Equal(JobRunStatus.TimedOut, completed.Status);
            Assert.IsType<JobTimeoutException>(completed.Exception);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task UncooperativeJob_RunsToCompletion_AndIsReportedHonestly()
    {
        // A job that ignores its token cannot be aborted. It really did finish its work, so
        // reporting a timeout would be a lie; the overrun is logged instead.
        var host = BuildHost(typeof(UncooperativeJob), j => j.TimeoutSeconds = 1);

        await using var _ = host.Provider;
        var scheduler = await StartAndFireAsync(host);

        try
        {
            Assert.True(await WaitAsync(host.Tracker.Finished.Task), "The job never reached a final status.");

            var completed = Assert.Single(host.Tracker.Completed);
            Assert.Equal(JobRunStatus.Succeeded, completed.Status);
            Assert.Equal("finished anyway", completed.Message);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task NoTimeoutConfigured_LongJobIsNotInterrupted()
    {
        var host = BuildHost(typeof(UncooperativeJob)); // TimeoutSeconds stays null

        await using var _ = host.Provider;
        var scheduler = await StartAndFireAsync(host);

        try
        {
            Assert.True(await WaitAsync(host.Tracker.Finished.Task), "The job never reached a final status.");
            Assert.Equal(JobRunStatus.Succeeded, Assert.Single(host.Tracker.Completed).Status);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }
}

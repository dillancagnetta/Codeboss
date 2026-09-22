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

public class JobStatusListenerTests
{
    // ── Test doubles ─────────────────────────────────────────────────────────

    public sealed class RecordingRepository : StubJobRepository, IServiceJobRepository
    {
        public ConcurrentQueue<JobRunStarted> Started { get; } = new();
        public ConcurrentQueue<JobRunCompleted> Completed { get; } = new();
        public TaskCompletionSource CompletedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ThrowOnStart { get; set; }

        private readonly ServiceJob _job;
        public RecordingRepository(ServiceJob job) => _job = job;

        public override Task<IReadOnlyList<ServiceJob>> GetActiveJobsAsync(int? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ServiceJob>>(new[] { _job });

        public override Task<ServiceJob> FindAsync(JobRef reference, CancellationToken ct = default)
            => Task.FromResult(_job);

        public Task MarkRunStartedAsync(JobRunStarted run, CancellationToken ct = default)
        {
            if (ThrowOnStart) throw new InvalidOperationException("database is down");
            Started.Enqueue(run);
            return Task.CompletedTask;
        }

        public Task MarkRunCompletedAsync(JobRunCompleted run, CancellationToken ct = default)
        {
            Completed.Enqueue(run);
            CompletedSignal.TrySetResult();
            return Task.CompletedTask;
        }
    }

    public sealed class RecordingObserver : IJobRunObserver
    {
        public ConcurrentQueue<JobRunCompleted> Seen { get; } = new();

        public Task OnCompletedAsync(IJobExecutionContext context, JobRunCompleted run, CancellationToken ct = default)
        {
            Seen.Enqueue(run);
            return Task.CompletedTask;
        }
    }

    public sealed class ThrowingObserver : IJobRunObserver
    {
        public Task OnCompletedAsync(IJobExecutionContext context, JobRunCompleted run, CancellationToken ct = default)
            => throw new InvalidOperationException("observer exploded");
    }

    public sealed class Signal
    {
        public TaskCompletionSource Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public class SucceedingJob(IServiceJobRepository repository, Signal signal, ILogger<SucceedingJob> logger)
        : CodeBossJob(repository, logger)
    {
        public override Task Execute(CancellationToken ct = default)
        {
            Result = "did the thing";
            signal.Ran.TrySetResult();
            return Task.CompletedTask;
        }
    }

    public class FailingJob(IServiceJobRepository repository, Signal signal, ILogger<FailingJob> logger)
        : CodeBossJob(repository, logger)
    {
        public override Task Execute(CancellationToken ct = default)
        {
            signal.Ran.TrySetResult();
            throw new InvalidOperationException("the job broke");
        }
    }

    // ── Host ─────────────────────────────────────────────────────────────────

    private sealed record Host(ServiceProvider Provider, RecordingRepository Repository, Signal Signal);

    private static Host BuildHost(Type jobType, Action<IServiceCollection> extra = null)
    {
        var job = new ServiceJob
        {
            Id = 42,
            JobKey = Guid.NewGuid(),
            Name = "StatusProbe",
            CronExpression = "0/1 * * * * ?",
            Class = jobType.FullName,
            Assembly = jobType.Assembly.GetName().Name,
            IsActive = true,
            LastStatus = "NotRun"
        };

        var repository = new RecordingRepository(job);
        var signal = new Signal();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDateTimeProvider>(new CodeBossDateTimeProvider(
            Options.Create(new DateTimeOptions { TimeZone = "UTC" }),
            new NullLogger<CodeBossDateTimeProvider>()));
        services.AddSingleton(signal);
        services.AddScoped(jobType);

        extra?.Invoke(services);

        services.AddCodeBossJobs(opt =>
        {
            opt.Repo = typeof(StubJobRepository);
            opt.PulseInterval = TimeSpan.FromSeconds(1);
            opt.ConfigureQuartz = q => q.SchedulerName = $"StatusListener_{Guid.NewGuid():N}";
        });

        services.AddSingleton<IServiceJobRepository>(repository);

        return new Host(services.BuildServiceProvider(), repository, signal);
    }

    private static async Task<IScheduler> StartAsync(Host host)
    {
        var scheduler = await host.Provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        return scheduler;
    }

    private static async Task<bool> WaitAsync(Task task, int seconds = 20)
        => ReferenceEquals(await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))), task);

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuccessfulJob_RecordsStartedThenSucceeded()
    {
        var host = BuildHost(typeof(SucceedingJob));
        await using var _ = host.Provider;
        var scheduler = await StartAsync(host);

        try
        {
            Assert.True(await WaitAsync(host.Repository.CompletedSignal.Task), "No completion was recorded.");

            var started = Assert.Single(host.Repository.Started);
            Assert.Equal(42, started.Job.Id);
            Assert.Equal(1, started.Attempt);
            Assert.False(string.IsNullOrEmpty(started.FireInstanceId));

            Assert.True(host.Repository.Completed.TryDequeue(out var completed));
            Assert.Equal(JobRunStatus.Succeeded, completed.Status);
            Assert.True(completed.IsSuccess);
            // The job's own Result becomes the run message.
            Assert.Equal("did the thing", completed.Message);
            Assert.Null(completed.Exception);
            // Start and completion correlate by fire id.
            Assert.Equal(started.FireInstanceId, completed.FireInstanceId);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task FailingJob_RecordsFailedWithTheUnwrappedCause()
    {
        var host = BuildHost(typeof(FailingJob));
        await using var _ = host.Provider;
        var scheduler = await StartAsync(host);

        try
        {
            Assert.True(await WaitAsync(host.Repository.CompletedSignal.Task), "No completion was recorded.");

            Assert.True(host.Repository.Completed.TryDequeue(out var completed));
            Assert.Equal(JobRunStatus.Failed, completed.Status);
            Assert.False(completed.IsSuccess);
            // Not the Quartz JobExecutionException wrapper — the real cause.
            Assert.Equal("the job broke", completed.Message);
            Assert.IsType<InvalidOperationException>(completed.Exception);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    /// <summary>
    /// Regression. Quartz skips a job outright when a job listener throws from
    /// <c>JobToBeExecuted</c> — it logs "(Job will NOT be executed!)". Status bookkeeping is not
    /// important enough to cost a run.
    /// </summary>
    [Fact]
    public async Task RepositoryThrowingOnStart_DoesNotPreventTheJobFromRunning()
    {
        var host = BuildHost(typeof(SucceedingJob));
        host.Repository.ThrowOnStart = true;
        await using var _ = host.Provider;
        var scheduler = await StartAsync(host);

        try
        {
            Assert.True(await WaitAsync(host.Signal.Ran.Task),
                "The job did not run because status bookkeeping failed.");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task SystemJobs_AreNotRecorded()
    {
        var host = BuildHost(typeof(SucceedingJob));
        await using var _ = host.Provider;
        var scheduler = await StartAsync(host);

        try
        {
            Assert.True(await WaitAsync(host.Repository.CompletedSignal.Task), "No completion was recorded.");

            // The pulse fires on the same schedule but has no ServiceJob row behind it.
            Assert.All(host.Repository.Started, s => Assert.Equal(42, s.Job.Id));
            Assert.All(host.Repository.Completed, c => Assert.Equal(42, c.Job.Id));
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Fact]
    public async Task Observers_ReceiveTheCompletedRun()
    {
        var observer = new RecordingObserver();
        var host = BuildHost(typeof(SucceedingJob), s =>
        {
            s.AddSingleton<IJobRunObserver>(new ThrowingObserver()); // registered first, on purpose
            s.AddSingleton<IJobRunObserver>(observer);
        });
        await using var _ = host.Provider;
        var scheduler = await StartAsync(host);

        try
        {
            Assert.True(await WaitAsync(host.Repository.CompletedSignal.Task), "No completion was recorded.");

            // A throwing observer must not suppress the ones after it.
            Assert.True(await WaitAsync(Task.Run(async () =>
            {
                while (observer.Seen.IsEmpty) await Task.Delay(50);
            })), "The second observer never ran after the first one threw.");

            Assert.True(observer.Seen.TryDequeue(out var run));
            Assert.Equal(JobRunStatus.Succeeded, run.Status);
            Assert.Equal(42, run.Job.Id);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    // ── Unit tests: option defaulting ────────────────────────────────────────

    [Theory]
    [InlineData(false, null, true)]   // nobody else listening -> library takes over
    [InlineData(true, null, false)]   // consumer has its own listener -> stay out of the way
    [InlineData(true, true, true)]    // explicit opt-in: run both
    [InlineData(false, false, false)] // explicit opt-out: run neither
    public void UseDefaultStatusListener_AutoDefault(bool consumerListener, bool? explicitSetting, bool expected)
    {
        var options = new CodeBossJobsOptions
        {
            RegisteredJobListener = consumerListener,
            UseDefaultStatusListener = explicitSetting
        };

        Assert.Equal(expected, options.ShouldUseDefaultStatusListener);
    }
}

using CodeBoss.AspNetCore.CbDateTime;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Model;
using Codeboss.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Quartz;

namespace CodeBoss.Jobs.Tests;

/// <summary>
/// End-to-end cover for the pulse registration: the unit tests assert what lands in
/// <see cref="QuartzOptions"/>, these assert that a real scheduler actually fires it. Phase 2 moved
/// the pulse from <c>ScheduleJob</c> + cron to a durable <c>AddJob</c> + repeating <c>AddTrigger</c>,
/// and a regression there stops ALL job scheduling silently.
/// </summary>
public class PulseSchedulingTests
{
    private static ServiceProvider BuildHost(
        IServiceJobRepository repository,
        Action<CodeBossJobsOptions> configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDateTimeProvider>(new CodeBossDateTimeProvider(
            Options.Create(new DateTimeOptions { TimeZone = "UTC" }),
            new NullLogger<CodeBossDateTimeProvider>()));

        services.AddCodeBossJobs(opt =>
        {
            opt.Repo = typeof(StubJobRepository); // superseded by the singleton registered below
            opt.PulseInterval = TimeSpan.FromSeconds(1);
            opt.ConfigureQuartz = q => q.SchedulerName = $"PulseTests_{Guid.NewGuid():N}";
            configure?.Invoke(opt);
        });

        // Last registration wins for resolution, so this supersedes AddCodeBossJobs' AddTransient.
        services.AddSingleton(repository);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Pulse_FiresOnItsInterval_AndReconcilesFromTheRepository()
    {
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Mock<IServiceJobRepository>();
        repository
            .Setup(r => r.GetActiveJobsAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ServiceJob>())
            .Callback(() => fired.TrySetResult());

        await using var provider = BuildHost(repository.Object);
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.Start();
        try
        {
            var completed = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.True(ReferenceEquals(completed, fired.Task), "The pulse did not fire within 20 seconds.");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Fact]
    public async Task Pulse_IsDurable_SoItCanBeTriggeredOnDemandWithNoTrigger()
    {
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Mock<IServiceJobRepository>();
        repository
            .Setup(r => r.GetActiveJobsAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ServiceJob>())
            .Callback(() => fired.TrySetResult());

        // Far-future first fire: only an explicit TriggerJob can run it.
        await using var provider = BuildHost(repository.Object, o =>
        {
            o.PulseInterval = TimeSpan.FromHours(12);
            o.PulseOnStartup = false;
        });

        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        try
        {
            var pulseKey = new JobKey(nameof(Jobs.JobPulse), Jobs.JobGroups.System);

            // Removing the only trigger would delete a non-durable job outright.
            foreach (var trigger in await scheduler.GetTriggersOfJob(pulseKey))
            {
                await scheduler.UnscheduleJob(trigger.Key);
            }

            Assert.True(await scheduler.CheckExists(pulseKey), "The pulse job was not stored durably.");

            await scheduler.TriggerJob(pulseKey);

            var completed = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.True(ReferenceEquals(completed, fired.Task), "The on-demand trigger did not run the pulse.");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }
}

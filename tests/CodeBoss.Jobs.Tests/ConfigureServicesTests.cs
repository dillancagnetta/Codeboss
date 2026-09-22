using CodeBoss.AspNetCore.CbDateTime;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Jobs;
using CodeBoss.Jobs.Model;
using CodeBoss.Jobs.Services;
using Codeboss.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quartz;
using Quartz.Impl.Triggers;

namespace CodeBoss.Jobs.Tests;

public class ConfigureServicesTests
{
    private static IConfiguration EmptyConfiguration()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

    private static IServiceCollection BuildServices(Action<CodeBossJobsOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddCodeBossJobs(EmptyConfiguration(), configure);
        return services;
    }

    [Fact]
    public void NullConfigure_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddCodeBossJobs(EmptyConfiguration(), null!));
    }

    [Fact]
    public void NullRepo_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddCodeBossJobs(EmptyConfiguration(), opt =>
            {
                // opt.Repo intentionally not set
            }));
    }

    [Fact]
    public void ValidOptions_RegistersIServiceJobRepository()
    {
        var services = BuildServices(opt => opt.Repo = typeof(StubJobRepository));

        var provider = services.BuildServiceProvider();
        var repo = provider.GetService<IServiceJobRepository>();

        Assert.NotNull(repo);
        Assert.IsType<StubJobRepository>(repo);
    }

    [Fact]
    public void ValidOptions_RegistersIServiceJobService()
    {
        var services = BuildServices(opt => opt.Repo = typeof(StubJobRepository));
        // ServiceJobQuartzService requires IDateTimeProvider which is consumer-supplied
        services.AddSingleton<IDateTimeProvider>(new CodeBossDateTimeProvider(
            Microsoft.Extensions.Options.Options.Create(new DateTimeOptions { TimeZone = "UTC" }),
            new NullLogger<CodeBossDateTimeProvider>()));

        var descriptor = services.FirstOrDefault(d => d.ImplementationType == typeof(ServiceJobQuartzService));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void ValidOptions_BindsCodeBossJobsOptions()
    {
        var services = BuildServices(opt =>
        {
            opt.Repo = typeof(StubJobRepository);
            opt.ConcurrentSchedulerOperations = 7;
            opt.MisfirePolicy = MisfirePolicy.FireAndProceed;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CodeBossJobsOptions>>().Value;

        Assert.Equal(7, options.ConcurrentSchedulerOperations);
        Assert.Equal(MisfirePolicy.FireAndProceed, options.MisfirePolicy);
    }

    [Fact]
    public void IsMultiTenantMode_True_OptionsBound()
    {
        var services = BuildServices(opt =>
        {
            opt.Repo = typeof(StubJobRepository);
            opt.IsMultiTenantMode = true;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CodeBossJobsOptions>>().Value;

        Assert.True(options.IsMultiTenantMode);
    }

    [Fact]
    public void IsMultiTenantMode_False_OptionsBound()
    {
        var services = BuildServices(opt =>
        {
            opt.Repo = typeof(StubJobRepository);
            opt.IsMultiTenantMode = false;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CodeBossJobsOptions>>().Value;

        Assert.False(options.IsMultiTenantMode);
    }

    [Fact]
    public void MaxConcurrency_ReflectsConcurrentSchedulerOperations()
    {
        var services = BuildServices(opt =>
        {
            opt.Repo = typeof(StubJobRepository);
            opt.ConcurrentSchedulerOperations = 4;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CodeBossJobsOptions>>().Value;

        Assert.Equal(4, options.ConcurrentSchedulerOperations);
    }

    // ── Pulse registration ───────────────────────────────────────────────────

    private static QuartzOptions QuartzOptionsFor(Action<CodeBossJobsOptions> configure)
    {
        var services = BuildServices(opt =>
        {
            opt.Repo = typeof(StubJobRepository);
            configure(opt);
        });

        return services.BuildServiceProvider().GetRequiredService<IOptions<QuartzOptions>>().Value;
    }

    [Theory]
    [InlineData(false, nameof(JobPulse))]
    [InlineData(true, nameof(MultiTenantJobPulse))]
    public void Pulse_IsRegisteredAsADurableJob(bool multiTenant, string expectedName)
    {
        var quartz = QuartzOptionsFor(o => o.IsMultiTenantMode = multiTenant);

        var pulse = Assert.Single(quartz.JobDetails);
        Assert.Equal(expectedName, pulse.Key.Name);
        Assert.Equal(JobGroups.System, pulse.Key.Group);
        // Durable: a job with no trigger survives, so "sync now" can TriggerJob against it.
        Assert.True(pulse.Durable);
    }

    [Fact]
    public void PulseTrigger_UsesConfiguredInterval()
    {
        var quartz = QuartzOptionsFor(o => o.PulseInterval = TimeSpan.FromMinutes(3));

        var trigger = Assert.IsAssignableFrom<ISimpleTrigger>(Assert.Single(quartz.Triggers));
        Assert.Equal(TimeSpan.FromMinutes(3), trigger.RepeatInterval);
        Assert.Equal(SimpleTriggerImpl.RepeatIndefinitely, trigger.RepeatCount);
    }

    [Fact]
    public void PulseTrigger_TargetsThePulseJob()
    {
        var quartz = QuartzOptionsFor(o => o.IsMultiTenantMode = true);

        var pulse = Assert.Single(quartz.JobDetails);
        var trigger = Assert.Single(quartz.Triggers);

        Assert.Equal(pulse.Key, trigger.JobKey);
        Assert.Equal(JobGroups.System, trigger.Key.Group);
    }

    [Fact]
    public void PulseOnStartup_True_StartsImmediately()
    {
        var quartz = QuartzOptionsFor(o =>
        {
            o.PulseInterval = TimeSpan.FromMinutes(30);
            o.PulseOnStartup = true;
        });

        var trigger = Assert.Single(quartz.Triggers);
        Assert.True(trigger.StartTimeUtc <= DateTimeOffset.UtcNow.AddSeconds(5));
    }

    [Fact]
    public void PulseOnStartup_False_DefersFirstRunByOneInterval()
    {
        var quartz = QuartzOptionsFor(o =>
        {
            o.PulseInterval = TimeSpan.FromMinutes(30);
            o.PulseOnStartup = false;
        });

        var trigger = Assert.Single(quartz.Triggers);
        Assert.True(trigger.StartTimeUtc > DateTimeOffset.UtcNow.AddMinutes(29));
    }

    [Fact]
    public void PulseTrigger_DoesNotReplayMissedFires()
    {
        var quartz = QuartzOptionsFor(_ => { });

        var trigger = Assert.Single(quartz.Triggers);
        // A missed pulse is not worth catching up: the next run reconciles current state anyway.
        Assert.Equal(MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount, trigger.MisfireInstruction);
    }

    // ── Job store ────────────────────────────────────────────────────────────

    [Fact]
    public void ByDefault_UsesInMemoryStore()
    {
        var quartz = QuartzOptionsFor(_ => { });

        Assert.Contains("RAMJobStore", quartz["quartz.jobStore.type"]);
    }

    [Fact]
    public void ConfigureQuartz_RunsAfterDefaults_AndCanReplaceTheJobStore()
    {
        var quartz = QuartzOptionsFor(o => o.ConfigureQuartz = q =>
            q.SetProperty("quartz.jobStore.type", "My.Custom.Store"));

        Assert.Equal("My.Custom.Store", quartz["quartz.jobStore.type"]);
    }

    [Fact]
    public void ConfigureQuartz_IsInvoked()
    {
        var invoked = false;
        QuartzOptionsFor(o => o.ConfigureQuartz = _ => invoked = true);

        Assert.True(invoked);
    }

    // ── Options validation and the legacy shim ───────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PulseInterval_NotPositive_Throws(int minutes)
    {
        var options = new CodeBossJobsOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.PulseInterval = TimeSpan.FromMinutes(minutes));
    }

    [Fact]
    public void PulseInterval_DefaultsToFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), new CodeBossJobsOptions().PulseInterval);
    }

#pragma warning disable CS0618 // exercising the obsolete compatibility shim on purpose
    [Theory]
    [InlineData(true, 15)]
    [InlineData(false, 1)]
    public void ProductionMode_MapsToPulseInterval(bool productionMode, int expectedMinutes)
    {
        var options = new CodeBossJobsOptions { ProductionMode = productionMode };

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), options.PulseInterval);
        Assert.Equal(productionMode, options.ProductionMode);
    }

    [Fact]
    public void ProductionMode_StillDrivesTheTrigger()
    {
        var quartz = QuartzOptionsFor(o => o.ProductionMode = true);

        var trigger = Assert.IsAssignableFrom<ISimpleTrigger>(Assert.Single(quartz.Triggers));
        Assert.Equal(TimeSpan.FromMinutes(15), trigger.RepeatInterval);
    }
#pragma warning restore CS0618

    [Fact]
    public void Overload_WithoutConfiguration_Works()
    {
        var services = new ServiceCollection();
        services.AddCodeBossJobs(opt => opt.Repo = typeof(StubJobRepository));

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IServiceJobRepository>());
        Assert.Single(provider.GetRequiredService<IOptions<QuartzOptions>>().Value.JobDetails);
    }
}

// Minimal stub satisfying the IServiceJobRepository interface
public class StubJobRepository : IServiceJobRepository
{
    public virtual Task<IReadOnlyList<ServiceJob>> GetActiveJobsAsync(int? tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ServiceJob>>([]);

    public virtual Task<ServiceJob> FindAsync(JobRef job, CancellationToken ct = default)
        => Task.FromResult<ServiceJob>(null!);

    public virtual Task SetStatusAsync(JobRef job, JobRunStatus status, string message, CancellationToken ct = default)
        => Task.CompletedTask;

    public virtual Task ClearStatusAsync(JobRef job, CancellationToken ct = default)
        => Task.CompletedTask;
}

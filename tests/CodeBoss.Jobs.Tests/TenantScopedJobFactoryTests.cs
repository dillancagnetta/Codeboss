using CodeBoss.AspNetCore.CbDateTime;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Jobs;
using CodeBoss.Jobs.Model;
using CodeBoss.MultiTenant;
using Codeboss.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Quartz;
using Quartz.Spi;

namespace CodeBoss.Jobs.Tests;

/// <summary>
/// The tenant context has to be in place BEFORE the job's constructor dependencies are resolved,
/// because a tenant-aware DbContext picks its connection string at construction. These tests drive a
/// real scheduler so they prove the ordering rather than assuming it.
/// </summary>
public class TenantScopedJobFactoryTests
{
    private const int TenantId = 5;
    internal static CapturingLoggerFactory Captured;

    // ── Test doubles ─────────────────────────────────────────────────────────

    /// <summary>Stands in for a tenant-aware DbContext: scoped, and reads the ambient tenant when constructed.</summary>
    public sealed class TenantAwareDependency(AmbientTenant ambient)
    {
        public int? TenantSeenAtConstruction { get; } = ambient.TenantId;
    }

    public sealed class AmbientTenant
    {
        public int? TenantId { get; set; }
    }

    public sealed class RecordingInitializer : IJobTenantScopeInitializer
    {
        public void Initialize(IServiceProvider scopedProvider, int tenantId, JobKey jobKey)
            => scopedProvider.GetRequiredService<AmbientTenant>().TenantId = tenantId;
    }

    public sealed class ThrowingInitializer : IJobTenantScopeInitializer
    {
        public void Initialize(IServiceProvider scopedProvider, int tenantId, JobKey jobKey)
            => throw new InvalidOperationException($"Unknown tenant {tenantId}");
    }

    /// <summary>Captures what the job saw, across scopes.</summary>
    public sealed class Probe
    {
        public TaskCompletionSource Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int? TenantSeenByDependency { get; set; }
        public int? TenantSeenByJob { get; set; }
    }

    public class TenantProbeJob(
        IServiceJobRepository repository,
        TenantAwareDependency dependency,
        Probe probe,
        ILogger<TenantProbeJob> logger) : CodeBossJob(repository, logger)
    {
        public override Task Execute(CancellationToken ct = default)
        {
            probe.TenantSeenByDependency = dependency.TenantSeenAtConstruction;
            probe.TenantSeenByJob = TenantId;
            probe.Ran.TrySetResult();
            return Task.CompletedTask;
        }
    }

    // ── Host ─────────────────────────────────────────────────────────────────

    private static ServiceProvider BuildHost(Probe probe, Action<IServiceCollection> extra = null)
    {
        var job = new ServiceJob
        {
            Id = 1,
            JobKey = Guid.NewGuid(),
            Name = "TenantProbe",
            CronExpression = "0/1 * * * * ?",
            Class = typeof(TenantProbeJob).FullName,
            Assembly = typeof(TenantProbeJob).Assembly.GetName().Name,
            IsActive = true,
            LastStatus = "NotRun"
        };

        var repository = new Mock<IServiceJobRepository>();
        repository.Setup(r => r.GetActiveJobsAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { job });
        repository.Setup(r => r.FindAsync(It.IsAny<JobRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var tenant = new Mock<ITenant>();
        tenant.Setup(t => t.Id).Returns(TenantId);
        tenant.Setup(t => t.Name).Returns($"Tenant{TenantId}");
        var tenants = new Mock<ISimpleTenantsProvider>();
        tenants.Setup(p => p.Tenants()).Returns(new[] { tenant.Object });

        var services = new ServiceCollection();
        var capture = new CapturingLoggerFactory();
        Captured = capture;
        services.AddSingleton<ILoggerFactory>(capture);
        services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
        services.AddSingleton(capture);
        services.AddSingleton<IDateTimeProvider>(new CodeBossDateTimeProvider(
            Options.Create(new DateTimeOptions { TimeZone = "UTC" }),
            new NullLogger<CodeBossDateTimeProvider>()));
        services.AddSingleton(tenants.Object);
        services.AddSingleton(probe);
        services.AddScoped<AmbientTenant>();
        services.AddScoped<TenantAwareDependency>();
        services.AddScoped<TenantProbeJob>();

        extra?.Invoke(services);

        services.AddCodeBossJobs(opt =>
        {
            opt.Repo = typeof(StubJobRepository);
            opt.IsMultiTenantMode = true;
            opt.PulseInterval = TimeSpan.FromSeconds(1);
            opt.ConfigureQuartz = q => q.SchedulerName = $"TenantFactory_{Guid.NewGuid():N}";
        });

        services.AddSingleton(repository.Object);

        return services.BuildServiceProvider();
    }

    private static async Task<IScheduler> StartAsync(ServiceProvider provider)
    {
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        return scheduler;
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void MultiTenantMode_RegistersTheTenantScopedJobFactory()
    {
        using var provider = BuildHost(new Probe());

        Assert.IsType<TenantScopedJobFactory>(provider.GetRequiredService<IJobFactory>());
    }

    [Fact]
    public void SingleTenantMode_LeavesTheDefaultJobFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddCodeBossJobs(opt =>
        {
            opt.Repo = typeof(StubJobRepository);
            opt.IsMultiTenantMode = false;
        });

        using var provider = services.BuildServiceProvider();

        Assert.IsNotType<TenantScopedJobFactory>(provider.GetRequiredService<IJobFactory>());
    }

    [Fact]
    public void AConsumerRegisteredJobFactory_IsNotOverwritten()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IJobFactory, CustomJobFactory>();
        services.AddCodeBossJobs(opt =>
        {
            opt.Repo = typeof(StubJobRepository);
            opt.IsMultiTenantMode = true;
        });

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CustomJobFactory>(provider.GetRequiredService<IJobFactory>());
    }

    [Fact]
    public async Task TenantContext_IsVisibleToTheJobsConstructorDependencies()
    {
        var probe = new Probe();
        await using var provider = BuildHost(probe, s => s.AddScoped<IJobTenantScopeInitializer, RecordingInitializer>());
        var scheduler = await StartAsync(provider);

        try
        {
            var completed = await Task.WhenAny(probe.Ran.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            Assert.True(ReferenceEquals(completed, probe.Ran.Task),
                "The tenant job never ran. Log: " + string.Join(" ~~ ", Captured.Entries));

            // The important assertion: the dependency read the tenant in ITS OWN constructor,
            // which runs after ConfigureScope and before the job body.
            Assert.Equal(TenantId, probe.TenantSeenByDependency);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Fact]
    public async Task NoInitializerRegistered_JobStillRunsWithoutTenantContext()
    {
        var probe = new Probe();
        await using var provider = BuildHost(probe); // no IJobTenantScopeInitializer
        var scheduler = await StartAsync(provider);

        try
        {
            var completed = await Task.WhenAny(probe.Ran.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            Assert.True(ReferenceEquals(completed, probe.Ran.Task), "The tenant job never ran.");
            Assert.Null(probe.TenantSeenByDependency);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Fact]
    public async Task InitializerThrows_TheJobNeverRuns()
    {
        var probe = new Probe();
        await using var provider = BuildHost(probe, s => s.AddScoped<IJobTenantScopeInitializer, ThrowingInitializer>());
        var scheduler = await StartAsync(provider);

        try
        {
            // Running against the wrong tenant's database is worse than not running at all.
            var completed = await Task.WhenAny(probe.Ran.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.False(ReferenceEquals(completed, probe.Ran.Task),
                "The job ran even though the tenant context could not be established.");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private sealed class CustomJobFactory : IJobFactory
    {
        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler) => throw new NotSupportedException();
        public void ReturnJob(IJob job) { }
    }
}

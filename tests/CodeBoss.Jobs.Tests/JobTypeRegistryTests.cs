using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Jobs;
using CodeBoss.Jobs.Model;
using CodeBoss.Jobs.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;

namespace CodeBoss.Jobs.Tests;

public class JobTypeRegistryTests
{
    [JobDefinitionId("tests.identified-job")]
    public class IdentifiedJob(IServiceJobRepository repository, ILogger<IdentifiedJob> logger)
        : CodeBossJob(repository, logger)
    {
        public override Task Execute(CancellationToken ct = default) => Task.CompletedTask;
    }

    public class UnidentifiedJob(IServiceJobRepository repository, ILogger<UnidentifiedJob> logger)
        : CodeBossJob(repository, logger)
    {
        public override Task Execute(CancellationToken ct = default) => Task.CompletedTask;
    }

    public abstract class AbstractJob(IServiceJobRepository repository, ILogger logger)
        : CodeBossJob(repository, logger);

    private static IJobTypeRegistry RegistryFor(params Type[] jobTypes)
    {
        var services = new ServiceCollection();
        foreach (var type in jobTypes) services.AddCodeBossJob(type);
        services.AddSingleton<IJobTypeRegistry, JobTypeRegistry>();
        return services.BuildServiceProvider().GetRequiredService<IJobTypeRegistry>();
    }

    // ── Resolution ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_ByStableId()
    {
        var registry = RegistryFor(typeof(IdentifiedJob));

        Assert.Equal(typeof(IdentifiedJob), registry.Resolve("tests.identified-job"));
    }

    [Fact]
    public void Resolve_ByStableId_IsCaseInsensitive()
    {
        var registry = RegistryFor(typeof(IdentifiedJob));

        Assert.Equal(typeof(IdentifiedJob), registry.Resolve("TESTS.IDENTIFIED-JOB"));
    }

    [Fact]
    public void Resolve_ARegisteredTypeByItsFullName_StillWorks()
    {
        // Rows written before ids existed store the full type name; they must keep resolving.
        var registry = RegistryFor(typeof(IdentifiedJob));

        Assert.Equal(typeof(IdentifiedJob), registry.Resolve(typeof(IdentifiedJob).FullName));
    }

    [Fact]
    public void Resolve_FallsBackToReflectionForUnregisteredTypes()
    {
        var registry = RegistryFor(); // nothing registered

        var resolved = registry.Resolve(typeof(UnidentifiedJob).FullName,
            typeof(UnidentifiedJob).Assembly.GetName().Name);

        Assert.Equal(typeof(UnidentifiedJob), resolved);
    }

    [Theory]
    [InlineData("No.Such.Type", "NoSuchAssembly")]
    [InlineData("No.Such.Type", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Resolve_UnknownType_ReturnsNullWithoutThrowing(string id, string assembly)
    {
        Assert.Null(RegistryFor(typeof(IdentifiedJob)).Resolve(id, assembly));
    }

    // ── IdFor ────────────────────────────────────────────────────────────────

    [Fact]
    public void IdFor_PrefersTheAttribute() =>
        Assert.Equal("tests.identified-job", RegistryFor().IdFor(typeof(IdentifiedJob)));

    [Fact]
    public void IdFor_FallsBackToFullName() =>
        Assert.Equal(typeof(UnidentifiedJob).FullName, RegistryFor().IdFor(typeof(UnidentifiedJob)));

    [Fact]
    public void IdFor_Null_IsNull() => Assert.Null(RegistryFor().IdFor(null));

    // ── Registration ─────────────────────────────────────────────────────────

    [Fact]
    public void AddCodeBossJob_RegistersTheTypeForDependencyInjection()
    {
        // Registering in DI means ValidateOnBuild proves the job's dependencies resolve at startup,
        // not at 3am on its first fire.
        var services = new ServiceCollection();
        services.AddCodeBossJob<IdentifiedJob>();

        Assert.Contains(services, d => d.ServiceType == typeof(IdentifiedJob));
    }

    [Fact]
    public void AddCodeBossJob_RejectsANonJobType()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddCodeBossJob(typeof(string)));
    }

    [Fact]
    public void AddCodeBossJobsFromAssembly_RegistersConcreteJobsOnly()
    {
        var services = new ServiceCollection();
        services.AddCodeBossJobsFromAssembly(typeof(JobTypeRegistryTests).Assembly);
        services.AddSingleton<IJobTypeRegistry, JobTypeRegistry>();

        var registry = services.BuildServiceProvider().GetRequiredService<IJobTypeRegistry>();

        Assert.Equal(typeof(IdentifiedJob), registry.Resolve("tests.identified-job"));
        Assert.Equal(typeof(UnidentifiedJob), registry.Resolve(typeof(UnidentifiedJob).FullName));
        Assert.DoesNotContain(registry.Registrations.Values, t => t == typeof(AbstractJob));
    }

    [Fact]
    public void Registry_IsRegisteredByAddCodeBossJobs()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddCodeBossJobs(opt => opt.Repo = typeof(StubJobRepository));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IJobTypeRegistry>());
    }

    // ── The scenario this phase exists for ───────────────────────────────────

    [Fact]
    public void ARenamedJobClass_StillResolvesThroughItsStableId()
    {
        // The row stores the id and NO assembly. Reflection could never resolve this — which is the
        // point: the class is free to move or be renamed without touching the database.
        var registry = RegistryFor(typeof(IdentifiedJob));
        var service = new ServiceJobQuartzService(jobTypeRegistry: registry);

        var job = new ServiceJob
        {
            Id = 1,
            JobKey = Guid.NewGuid(),
            Name = "Renamed",
            CronExpression = "0 0 6 * * ?",
            Class = "tests.identified-job",
            Assembly = null
        };

        var detail = service.BuildQuartzJob(job);

        Assert.NotNull(detail);
        Assert.Equal(typeof(IdentifiedJob), detail.JobType);
    }

    [Fact]
    public void WithNoRegistry_LegacyRowsStillResolve()
    {
        // Existing consumers that never call AddCodeBossJob keep working unchanged.
        var service = new ServiceJobQuartzService();

        var job = new ServiceJob
        {
            Id = 1,
            JobKey = Guid.NewGuid(),
            Name = "Legacy",
            CronExpression = "0 0 6 * * ?",
            Class = typeof(UnidentifiedJob).FullName,
            Assembly = typeof(UnidentifiedJob).Assembly.GetName().Name
        };

        Assert.Equal(typeof(UnidentifiedJob), service.BuildQuartzJob(job)?.JobType);
    }

    [Fact]
    public void ResolveJobType_UnknownRow_ReturnsNullSoCallersCanRecordTheError()
    {
        var service = new ServiceJobQuartzService(jobTypeRegistry: RegistryFor(typeof(IdentifiedJob)));

        var job = new ServiceJob { Class = "does.not.exist", Assembly = "Nope" };

        Assert.Null(service.ResolveJobType(job));
        Assert.Null(service.BuildQuartzJob(job));
    }

    [Fact]
    public void IServiceJobService_DefaultResolveJobType_UsesReflection()
    {
        // A consumer's own IServiceJobService that predates this method keeps the legacy behaviour.
        IServiceJobService service = new LegacyService();

        var job = new ServiceJob
        {
            Class = typeof(UnidentifiedJob).FullName,
            Assembly = typeof(UnidentifiedJob).Assembly.GetName().Name
        };

        Assert.Equal(typeof(UnidentifiedJob), service.ResolveJobType(job));
    }

    private sealed class LegacyService : IServiceJobService
    {
        public IJobDetail BuildQuartzJob(ServiceJob job) => null;
        public ITrigger BuildQuartzTrigger(ServiceJob job) => null;
        public ITrigger BuildJobTrigger(ServiceJob job, int? tenantId) => null;
        public IJobDetail BuildQuartzJob(ServiceJob job, int? tenantId) => null;
        public JobKey GetJobKey(ServiceJob job, int? tenantId) => new("x");
    }
}

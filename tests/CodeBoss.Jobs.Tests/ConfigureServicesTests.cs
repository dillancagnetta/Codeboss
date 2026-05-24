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
}

// Minimal stub satisfying the IServiceJobRepository interface
public class StubJobRepository : IServiceJobRepository
{
    public Task<IEnumerable<ServiceJob>> GetActiveJobsAsync(CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<ServiceJob>());

    public Task AddOrUpdateAsync(ServiceJob job, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(int id, CancellationToken ct = default) => Task.CompletedTask;
    public Task<ServiceJob> GetByIdAsync(int id, CancellationToken ct = default) => Task.FromResult<ServiceJob>(null!);
    public Task UpdateLastStatusMessageAsync(int serviceJobId, string statusMessage, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdateStatusMessagesAsync(int serviceJobId, string message, string status, CancellationToken ct = default) => Task.CompletedTask;
    public Task ClearStatusesAsync(ServiceJob job, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IEnumerable<ServiceJob>> GetActiveJobsAsync(int? tenantId, CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<ServiceJob>());

    public Task<ServiceJob> GetByIdAsync(int id, int? tenantId, CancellationToken ct = default) => Task.FromResult<ServiceJob>(null!);
    public Task UpdateLastStatusMessageAsync(int serviceJobId, int? tenantId, string statusMessage, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdateStatusMessagesAsync(int serviceJobId, int? tenantId, string message, string status, CancellationToken ct = default) => Task.CompletedTask;
    public Task ClearStatusesAsync(ServiceJob job, int? tenantId, CancellationToken ct = default) => Task.CompletedTask;
}

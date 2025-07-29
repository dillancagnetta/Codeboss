using System.Reflection;
using CodeBoss.AspNetCore.CbDateTime;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Jobs;
using CodeBoss.Jobs.Model;
using CodeBoss.Jobs.Services;
using CodeBoss.MultiTenant;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Xunit.Abstractions;

namespace CodeBoss.Jobs.Tests;

/// <summary>
/// Tests using real in-memory Quartz.NET scheduler - much cleaner!
/// </summary>
public class InMemoryQuartzJobPulseTests : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IScheduler _scheduler;
    private readonly Mock<IServiceJobRepository> _mockRepository;
    private readonly IServiceJobService _service;
    private readonly Mock<ISimpleTenantsProvider> _mockTenantsProvider;
    private readonly Mock<ILogger<CodeBossJob>> _mockLogger;

    public InMemoryQuartzJobPulseTests(ITestOutputHelper output)
    {
        _output = output;
        var dateOptions = Options.Create(new DateTimeOptions { TimeZone = "South Africa Standard Time" });
        
        // Create real in-memory Quartz scheduler
        var schedulerFactory = new StdSchedulerFactory();
        _scheduler = schedulerFactory.GetScheduler().Result;
        _scheduler.Start().Wait();

        // Only mock what we need to
        _mockRepository = new Mock<IServiceJobRepository>();
        _service = new ServiceJobQuartzService(new CodeBossDateTimeProvider(dateOptions, new NullLogger<CodeBossDateTimeProvider>()));
        _mockTenantsProvider = new Mock<ISimpleTenantsProvider>();
        _mockLogger = new Mock<ILogger<CodeBossJob>>();
    }

    [Fact]
    public async Task SingleTenant_WithNewJobs_SchedulesCorrectly()
    {
        // Arrange
        var tenant = CreateTenant(1, "Tenant1");
        var jobs = CreateJobs(3, tenantId: 1);
        
        SetupMocks(new[] { tenant }, new Dictionary<int, List<ServiceJob>> { { 1, jobs } });
        
        var jobPulse = CreateJobPulse();

        // Act
        await jobPulse.Execute(CancellationToken.None);

        // Assert
        var scheduledJobs = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_1"));
        
        _output.WriteLine($"Result: {jobPulse.Result}");
        _output.WriteLine($"Scheduled jobs count: {scheduledJobs.Count}");
        
        Assert.Equal(3, scheduledJobs.Count);
        Assert.Contains("3 schedule(s)", jobPulse.Result);

        // Verify the jobs are actually scheduled with correct details
        foreach (var jobKey in scheduledJobs)
        {
            var jobDetail = await _scheduler.GetJobDetail(jobKey);
            var triggers = await _scheduler.GetTriggersOfJob(jobKey);
            
            Assert.NotNull(jobDetail);
            Assert.Single(triggers);
            Assert.Contains("tenant_1", jobKey.Group);
            
            _output.WriteLine($"Scheduled job: {jobKey.Name} in group {jobKey.Group}");
        }
    }

    [Fact]
    public async Task ExistingScheduledJobs_GetDeletedWhenInactive()
    {
        // Arrange
        var tenant = CreateTenant(1, "Tenant1");
        var activeJobs = CreateJobs(2, tenantId: 1);
        
        // Pre-schedule some jobs (simulate existing state)
        var extraJob = CreateJobs(1, tenantId: 1, startId: 9999).First(); // Different ID so it won't match
        var jobToDelete = _service.BuildQuartzJob(extraJob, 1);
        var triggerToDelete = _service.BuildJobTrigger(extraJob, 1);
        
        await _scheduler.ScheduleJob(jobToDelete, triggerToDelete);
        
        SetupMocks(new[] { tenant }, new Dictionary<int, List<ServiceJob>> { { 1, activeJobs } });
        
        var jobPulse = CreateJobPulse();

        // Act
        var beforeCount = (await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_1"))).Count;
        await jobPulse.Execute(CancellationToken.None);
        var afterCount = (await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_1"))).Count;

        // Assert
        _output.WriteLine($"Before: {beforeCount} jobs, After: {afterCount} jobs");
        _output.WriteLine($"Result: {jobPulse.Result}");
        
        Assert.Equal(1, beforeCount); // Had 1 pre-existing job
        Assert.Equal(2, afterCount);  // Now has 2 active jobs (1 deleted, 2 added)
        Assert.Contains("1 job schedule(s)", jobPulse.Result); // 1 deleted
        Assert.Contains("2 schedule(s)", jobPulse.Result); // 2 scheduled
    }

    [Fact]
    public async Task JobsWithChangedCronExpression_GetRescheduled()
    {
        // Arrange
        var tenant = CreateTenant(1, "Tenant1");
        var jobs = CreateJobs(1, tenantId: 1);
        var originalJob = jobs.First();
        
        // Schedule job with original cron
        originalJob.CronExpression = "0 0 12 * * ?"; // Daily at noon
        var jobDetail = _service.BuildQuartzJob(originalJob, 1);
        var trigger = _service.BuildJobTrigger(originalJob, 1);
        await _scheduler.ScheduleJob(jobDetail, trigger);
        
        // Now change the cron expression
        originalJob.CronExpression = "0 */5 * * * ?"; // Every 5 minutes
        
        SetupMocks(new[] { tenant }, new Dictionary<int, List<ServiceJob>> { { 1, jobs } });
        
        var jobPulse = CreateJobPulse();

        // Act
        await jobPulse.Execute(CancellationToken.None);

        // Assert
        var scheduledJobs = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_1"));
        var jobKey = scheduledJobs.First();
        var triggers = await _scheduler.GetTriggersOfJob(jobKey);
        var cronTrigger = triggers.OfType<ICronTrigger>().First();
        
        _output.WriteLine($"Result: {jobPulse.Result}");
        _output.WriteLine($"Updated cron expression: {cronTrigger.CronExpressionString}");
        
        Assert.Equal("0 */5 * * * ?", cronTrigger.CronExpressionString);
        Assert.Contains("1 schedule(s)", jobPulse.Result); // Should show as updated
    }

    [Fact]
    public async Task MultipleTenants_ProcessIndependently()
    {
        // Arrange
        var tenants = new[]
        {
            CreateTenant(1, "Tenant1"),
            CreateTenant(2, "Tenant2"),
            CreateTenant(3, "Tenant3")
        };

        var jobsPerTenant = new Dictionary<int, List<ServiceJob>>
        {
            { 1, CreateJobs(2, 1) },
            { 2, CreateJobs(3, 2) },
            { 3, CreateJobs(1, 3) }
        };

        SetupMocks(tenants, jobsPerTenant);
        
        var jobPulse = CreateJobPulse();

        // Act
        await jobPulse.Execute(CancellationToken.None);

        // Assert
        var tenant1Jobs = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_1"));
        var tenant2Jobs = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_2"));
        var tenant3Jobs = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_3"));

        _output.WriteLine($"Result: {jobPulse.Result}");
        _output.WriteLine($"Tenant 1: {tenant1Jobs.Count} jobs");
        _output.WriteLine($"Tenant 2: {tenant2Jobs.Count} jobs");
        _output.WriteLine($"Tenant 3: {tenant3Jobs.Count} jobs");
        
        Assert.Equal(2, tenant1Jobs.Count);
        Assert.Equal(3, tenant2Jobs.Count);
        Assert.Equal(1, tenant3Jobs.Count);
        Assert.Contains("6 schedule(s)", jobPulse.Result); // Total: 2+3+1
    }

    [Fact]
    public async Task TenantWithDatabaseError_DoesNotAffectOthers()
    {
        // Arrange
        var tenants = new[]
        {
            CreateTenant(1, "GoodTenant"),
            CreateTenant(2, "BadTenant"),
            CreateTenant(3, "AnotherGoodTenant")
        };

        SetupMocks(tenants, new Dictionary<int, List<ServiceJob>>
        {
            { 1, CreateJobs(2, 1) },
            { 3, CreateJobs(2, 3) }
        });

        // Make tenant 2 throw an error
        _mockRepository.Setup(r => r.GetActiveJobsAsync(2, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failed for tenant 2"));

        var jobPulse = CreateJobPulse();

        // Act
        await jobPulse.Execute(CancellationToken.None);

        // Assert
        var tenant1Jobs = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_1"));
        var tenant2Jobs = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_2"));
        var tenant3Jobs = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_3"));

        _output.WriteLine($"Result: {jobPulse.Result}");
        _output.WriteLine($"Good tenants processed despite tenant 2 error");
        
        Assert.Equal(2, tenant1Jobs.Count); // Good tenant 1 processed
        Assert.Equal(0, tenant2Jobs.Count); // Bad tenant 2 had no jobs scheduled
        Assert.Equal(2, tenant3Jobs.Count); // Good tenant 3 processed
        Assert.Contains("4 schedule(s)", jobPulse.Result); // 2+2 from good tenants
    }

    [Fact]
    public async Task LargeScale_HandlesEfficiently()
    {
        // Arrange
        const int tenantCount = 20;
        const int jobsPerTenant = 5;
        
        var tenants = Enumerable.Range(1, tenantCount).Select(i => CreateTenant(i, $"Tenant{i}")).ToArray();
        var jobsPerTenantDict = tenants.ToDictionary(t => t.Id, t => CreateJobs(jobsPerTenant, t.Id));

        SetupMocks(tenants, jobsPerTenantDict);
        
        var jobPulse = CreateJobPulse();

        // Act
        var startTime = DateTime.Now;
        await jobPulse.Execute(CancellationToken.None);
        var duration = DateTime.Now - startTime;

        // Assert
        var totalExpectedJobs = tenantCount * jobsPerTenant;
        var allScheduledJobs = new List<JobKey>();
        
        for (int i = 1; i <= tenantCount; i++)
        {
            var tenantJobs = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals($"tenant_{i}"));
            allScheduledJobs.AddRange(tenantJobs);
        }

        _output.WriteLine($"Scale test: {tenantCount} tenants, {jobsPerTenant} jobs each");
        _output.WriteLine($"Expected: {totalExpectedJobs}, Actual: {allScheduledJobs.Count}");
        _output.WriteLine($"Duration: {duration.TotalMilliseconds}ms");
        _output.WriteLine($"Throughput: {totalExpectedJobs / duration.TotalSeconds:F2} jobs/second");
        
        Assert.Equal(totalExpectedJobs, allScheduledJobs.Count);
        Assert.Contains($"{totalExpectedJobs} schedule(s)", jobPulse.Result);
        Assert.True(duration.TotalSeconds < 10, "Should handle large loads efficiently");
    }

    [Fact]
    public async Task InvalidCronExpression_HandledGracefully()
    {
        // Arrange
        var tenant = CreateTenant(1, "Tenant1");
        var jobs = CreateJobs(2, tenantId: 1);
        
        // Make one job have invalid cron expression
        jobs[0].CronExpression = "INVALID_CRON"; // fallback to NeverScheduledCronExpression
        jobs[1].CronExpression = "0 */5 * * * ?"; // Valid cron
        
        SetupMocks(new[] { tenant }, new Dictionary<int, List<ServiceJob>> { { 1, jobs } });
        
        var jobPulse = CreateJobPulse();

        // Act
        await jobPulse.Execute(CancellationToken.None);

        // Assert
        var scheduledJobs = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_1"));
        
        _output.WriteLine($"Result: {jobPulse.Result}");
        _output.WriteLine($"Scheduled jobs despite invalid cron: {scheduledJobs.Count}");
        
        // Both jobs should be scheduled (invalid cron becomes "never run" cron)
        Assert.Equal(2, scheduledJobs.Count);
        
        // Check that invalid cron was converted to "never run"
        foreach (var jobKey in scheduledJobs)
        {
            var triggers = await _scheduler.GetTriggersOfJob(jobKey);
            var cronTrigger = triggers.OfType<ICronTrigger>().First();
            _output.WriteLine($"Job {jobKey.Name} cron: {cronTrigger.CronExpressionString}");
        }
    }

    // Helper methods
    private MultiTenantJobPulse CreateJobPulse()
    {
        var jobPulse = new MultiTenantJobPulse(
            _mockRepository.Object,
            _service,
            _mockTenantsProvider.Object,
            Options.Create(new CodeBossJobsOptions()),
            _mockLogger.Object);

        // Set the real scheduler as its a protected property
        var schedulerProperty = typeof(CodeBossJob).GetProperty("Scheduler", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        schedulerProperty?.SetValue(jobPulse, _scheduler);

        return jobPulse;
    }

    private void SetupMocks(ITenant[] tenants, Dictionary<int, List<ServiceJob>> jobsPerTenant)
    {
        _mockTenantsProvider.Setup(p => p.Tenants()).Returns(tenants);

        foreach (var kvp in jobsPerTenant)
        {
            _mockRepository.Setup(r => r.GetActiveJobsAsync(kvp.Key, It.IsAny<CancellationToken>()))
                .ReturnsAsync(kvp.Value);
        }
    }

    private ITenant CreateTenant(int id, string name)
    {
        var mock = new Mock<ITenant>();
        mock.Setup(t => t.Id).Returns(id);
        mock.Setup(t => t.Name).Returns(name);
        return mock.Object;
    }

    private List<ServiceJob> CreateJobs(int count, int tenantId, int startId = 1)
    {
        return Enumerable.Range(startId, count).Select(i => new ServiceJob
        {
            Id = i + (tenantId * 1000),
            JobKey = Guid.NewGuid(),
            Name = $"Job{i}_Tenant{tenantId}",
            CronExpression = "0 */5 * * * ?",
            Class = "CodeBoss.Jobs.Tests.TestJob",
            Assembly = "CodeBoss.Jobs.Tests",
            LastStatus = "NotRun",
            IsActive = true
        }).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        if (_scheduler != null)
        {
            await _scheduler.Shutdown();
        }
    }
}

// Supporting classes
public class TestJob : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        return Task.CompletedTask;
    }
}

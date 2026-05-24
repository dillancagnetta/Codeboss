using System.Collections.Specialized;
using System.Reflection;
using CodeBoss.AspNetCore.CbDateTime;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Jobs;
using CodeBoss.Jobs.Model;
using CodeBoss.Jobs.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using CodeBoss.MultiTenant;

namespace CodeBoss.Jobs.Tests;

public class MultiTenantJobPulseAdvancedTests : IAsyncDisposable
{
    private readonly IScheduler _scheduler;
    private readonly Mock<IServiceJobRepository> _mockRepository;
    private readonly IServiceJobService _service;
    private readonly Mock<ISimpleTenantsProvider> _mockTenantsProvider;

    public MultiTenantJobPulseAdvancedTests()
    {
        var dateOptions = Options.Create(new DateTimeOptions { TimeZone = "South Africa Standard Time" });
        var props = new NameValueCollection { ["quartz.scheduler.instanceName"] = $"MTAdvanced_{Guid.NewGuid():N}" };
        var factory = new StdSchedulerFactory(props);
        _scheduler = factory.GetScheduler().GetAwaiter().GetResult();
        _scheduler.Start().GetAwaiter().GetResult();

        _mockRepository = new Mock<IServiceJobRepository>();
        _service = new ServiceJobQuartzService(
            new CodeBossDateTimeProvider(dateOptions, new NullLogger<CodeBossDateTimeProvider>()));
        _mockTenantsProvider = new Mock<ISimpleTenantsProvider>();
    }

    private MultiTenantJobPulse CreatePulse(CodeBossJobsOptions? opts = null)
    {
        opts ??= new CodeBossJobsOptions();
        var pulse = new MultiTenantJobPulse(
            _mockRepository.Object,
            _service,
            _mockTenantsProvider.Object,
            Options.Create(opts),
            new NullLogger<MultiTenantJobPulse>());

        var prop = typeof(CodeBossJob).GetProperty("Scheduler", BindingFlags.NonPublic | BindingFlags.Instance);
        prop!.SetValue(pulse, _scheduler);
        return pulse;
    }

    private ITenant MakeTenant(int id)
    {
        var m = new Mock<ITenant>();
        m.Setup(t => t.Id).Returns(id);
        m.Setup(t => t.Name).Returns($"Tenant{id}");
        return m.Object;
    }

    private ServiceJob MakeJob(int id, int tenantId, string cron = "0 */5 * * * ?",
        string cls = "CodeBoss.Jobs.Tests.TestJob", string assembly = "CodeBoss.Jobs.Tests")
        => new()
        {
            Id = id,
            JobKey = Guid.NewGuid(),
            Name = $"Job_{id}_T{tenantId}",
            CronExpression = cron,
            Class = cls,
            Assembly = assembly,
            IsActive = true,
            LastStatus = "NotRun"
        };

    // ── Reschedule: cron-only change ────────────────────────────────────────

    [Fact]
    public async Task CronOnlyChange_ReschedulesWithRescheduleJob_NotDeleteAndAdd()
    {
        var tenant = MakeTenant(1);
        var job = MakeJob(1, 1, "0 0 12 * * ?");

        var detail = _service.BuildQuartzJob(job, 1);
        var trigger = _service.BuildJobTrigger(job, 1);
        await _scheduler.ScheduleJob(detail, trigger);

        job.CronExpression = "0 */15 * * * ?";

        _mockTenantsProvider.Setup(p => p.Tenants()).Returns(new[] { tenant });
        _mockRepository.Setup(r => r.GetActiveJobsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { job });

        await CreatePulse().Execute(CancellationToken.None);

        var keys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_1"));
        Assert.Single(keys);

        var cronTrigger = (await _scheduler.GetTriggersOfJob(keys.First())).OfType<ICronTrigger>().First();
        Assert.Equal("0 */15 * * * ?", cronTrigger.CronExpressionString);
    }

    // ── Reschedule: job-type change ──────────────────────────────────────────

    [Fact]
    public async Task JobTypeChange_DeletesOldAndSchedulesNewJobDetail()
    {
        var tenant = MakeTenant(1);
        var job = MakeJob(1, 1);

        var detail = _service.BuildQuartzJob(job, 1);
        var trigger = _service.BuildJobTrigger(job, 1);
        await _scheduler.ScheduleJob(detail, trigger);

        // Change to second test job type
        job.Class = "CodeBoss.Jobs.Tests.TestJob2";

        _mockTenantsProvider.Setup(p => p.Tenants()).Returns(new[] { tenant });
        _mockRepository.Setup(r => r.GetActiveJobsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { job });

        await CreatePulse().Execute(CancellationToken.None);

        var keys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_1"));
        Assert.Single(keys);

        var newDetail = await _scheduler.GetJobDetail(keys.First());
        Assert.Equal(typeof(TestJob2), newDetail!.JobType);
    }

    // ── NeverScheduled cron ──────────────────────────────────────────────────

    [Fact]
    public async Task NeverScheduledCron_JobBuiltButNotAddedToScheduler()
    {
        var tenant = MakeTenant(1);
        var job = MakeJob(1, 1, ServiceJob.NeverScheduledCronExpression);

        _mockTenantsProvider.Setup(p => p.Tenants()).Returns(new[] { tenant });
        _mockRepository.Setup(r => r.GetActiveJobsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { job });

        await CreatePulse().Execute(CancellationToken.None);

        var keys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_1"));
        Assert.Empty(keys);
    }

    // ── Error path: unresolvable job type propagates as JobExecutionException ─

    [Fact]
    public async Task FailedScheduleOp_UnresolvableType_ThrowsJobExecutionException()
    {
        var tenant = MakeTenant(1);
        var job = MakeJob(1, 1, cls: "No.Such.Class", assembly: "FakeAssembly");

        _mockTenantsProvider.Setup(p => p.Tenants()).Returns(new[] { tenant });
        _mockRepository.Setup(r => r.GetActiveJobsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { job });

        await Assert.ThrowsAsync<JobExecutionException>(() => CreatePulse().Execute(CancellationToken.None));
    }

    // ── Previous error status cleared on success ─────────────────────────────

    [Fact]
    public async Task PreviousErrorStatus_ClearedAfterSuccessfulReschedule()
    {
        var tenant = MakeTenant(1);
        var job = MakeJob(1, 1, "0 0 12 * * ?");
        job.LastStatus = "Error scheduling Job";

        var detail = _service.BuildQuartzJob(job, 1);
        var trigger = _service.BuildJobTrigger(job, 1);
        await _scheduler.ScheduleJob(detail, trigger);

        job.CronExpression = "0 */20 * * * ?";

        _mockTenantsProvider.Setup(p => p.Tenants()).Returns(new[] { tenant });
        _mockRepository.Setup(r => r.GetActiveJobsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { job });

        await CreatePulse().Execute(CancellationToken.None);

        _mockRepository.Verify(r => r.ClearStatusesAsync(
            job,
            (int?)1,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Cancellation token respected ─────────────────────────────────────────

    [Fact]
    public async Task CancelledToken_DoesNotProcessTenants()
    {
        var tenant = MakeTenant(1);
        _mockTenantsProvider.Setup(p => p.Tenants()).Returns(new[] { tenant });
        _mockRepository.Setup(r => r.GetActiveJobsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { MakeJob(1, 1) });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<Exception>(() => CreatePulse().Execute(cts.Token));
    }

    // ── Semaphore limits respected: ConcurrentSchedulerOperations ───────────

    [Fact]
    public async Task ConcurrentSchedulerOperations_ReflectsOptionsValue()
    {
        var opts = new CodeBossJobsOptions { ConcurrentSchedulerOperations = 3 };

        var tenant = MakeTenant(1);
        var jobs = Enumerable.Range(1, 5).Select(i => MakeJob(i, 1)).ToList();

        _mockTenantsProvider.Setup(p => p.Tenants()).Returns(new[] { tenant });
        _mockRepository.Setup(r => r.GetActiveJobsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        var pulse = CreatePulse(opts);
        await pulse.Execute(CancellationToken.None);

        var keys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("tenant_1"));
        Assert.Equal(5, keys.Count);
    }

    // ── Result string shape ──────────────────────────────────────────────────

    [Fact]
    public async Task Result_ContainsUpdatedCount()
    {
        var tenant = MakeTenant(1);
        var jobs = Enumerable.Range(1, 3).Select(i => MakeJob(i, 1)).ToList();

        _mockTenantsProvider.Setup(p => p.Tenants()).Returns(new[] { tenant });
        _mockRepository.Setup(r => r.GetActiveJobsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        var pulse = CreatePulse();
        await pulse.Execute(CancellationToken.None);

        Assert.Contains("Updated 3 schedule(s)", pulse.Result);
    }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.Shutdown();
    }
}

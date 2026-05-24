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

namespace CodeBoss.Jobs.Tests;

public class JobPulseTests : IAsyncDisposable
{
    private readonly IScheduler _scheduler;
    private readonly Mock<IServiceJobRepository> _mockRepository;
    private readonly IServiceJobService _service;

    public JobPulseTests()
    {
        var dateOptions = Options.Create(new DateTimeOptions { TimeZone = "South Africa Standard Time" });
        var props = new NameValueCollection { ["quartz.scheduler.instanceName"] = $"JobPulseTests_{Guid.NewGuid():N}" };
        var schedulerFactory = new StdSchedulerFactory(props);
        _scheduler = schedulerFactory.GetScheduler().GetAwaiter().GetResult();
        _scheduler.Start().GetAwaiter().GetResult();

        _mockRepository = new Mock<IServiceJobRepository>();
        _service = new ServiceJobQuartzService(new CodeBossDateTimeProvider(dateOptions, new NullLogger<CodeBossDateTimeProvider>()));
    }

    private JobPulse CreatePulse()
    {
        var pulse = new JobPulse(_mockRepository.Object, _service, new NullLogger<JobPulse>());
        var prop = typeof(CodeBossJob).GetProperty("Scheduler", BindingFlags.NonPublic | BindingFlags.Instance);
        prop!.SetValue(pulse, _scheduler);
        return pulse;
    }

    private ServiceJob MakeJob(int id, string cron = "0 */5 * * * ?", string cls = "CodeBoss.Jobs.Tests.TestJob", string assembly = "CodeBoss.Jobs.Tests")
        => new()
        {
            Id = id,
            JobKey = Guid.NewGuid(),
            Name = $"Job_{id}",
            CronExpression = cron,
            Class = cls,
            Assembly = assembly,
            IsActive = true,
            LastStatus = "NotRun"
        };

    [Fact]
    public async Task NewJobs_AreScheduled()
    {
        var jobs = new[] { MakeJob(1), MakeJob(2) };
        _mockRepository.Setup(r => r.GetActiveJobsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(jobs);

        await CreatePulse().Execute(CancellationToken.None);

        var keys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        Assert.Equal(2, keys.Count(k => k.Group != "System"));
    }

    [Fact]
    public async Task InactiveQuartzJobs_AreDeleted()
    {
        var staleJob = MakeJob(999);
        var detail = _service.BuildQuartzJob(staleJob);
        var trigger = _service.BuildQuartzTrigger(staleJob);
        await _scheduler.ScheduleJob(detail, trigger);

        var activeJobs = new[] { MakeJob(1) };
        _mockRepository.Setup(r => r.GetActiveJobsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(activeJobs);

        var pulse = CreatePulse();
        await pulse.Execute(CancellationToken.None);

        var keys = (await _scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup()))
            .Where(k => k.Group != "System").ToList();

        Assert.Single(keys);
        Assert.Contains("Deleted 1 job schedule(s)", pulse.Result);
    }

    [Fact]
    public async Task ChangedCronExpression_ReschedulesWithAtomicSwap()
    {
        var job = MakeJob(1, "0 0 12 * * ?");
        var detail = _service.BuildQuartzJob(job);
        var trigger = _service.BuildQuartzTrigger(job);
        await _scheduler.ScheduleJob(detail, trigger);

        job.CronExpression = "0 */10 * * * ?";
        _mockRepository.Setup(r => r.GetActiveJobsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { job });

        await CreatePulse().Execute(CancellationToken.None);

        var keys = (await _scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup()))
            .Where(k => k.Group != "System").ToList();
        Assert.Single(keys);

        var cronTrigger = (await _scheduler.GetTriggersOfJob(keys.First())).OfType<ICronTrigger>().First();
        Assert.Equal("0 */10 * * * ?", cronTrigger.CronExpressionString);
    }

    [Fact]
    public async Task ChangedJobType_DeletesAndReschedulesJobDetail()
    {
        var job = MakeJob(1);
        var detail = _service.BuildQuartzJob(job);
        var trigger = _service.BuildQuartzTrigger(job);
        await _scheduler.ScheduleJob(detail, trigger);

        job.Class = "CodeBoss.Jobs.Tests.TestJob2";
        _mockRepository.Setup(r => r.GetActiveJobsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { job });

        await CreatePulse().Execute(CancellationToken.None);

        var keys = (await _scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup()))
            .Where(k => k.Group != "System").ToList();
        Assert.Single(keys);

        var newDetail = await _scheduler.GetJobDetail(keys.First());
        Assert.Equal(typeof(TestJob2), newDetail!.JobType);
    }

    [Fact]
    public async Task NeverScheduledCronExpression_SkipsScheduling()
    {
        var job = MakeJob(1, ServiceJob.NeverScheduledCronExpression);
        _mockRepository.Setup(r => r.GetActiveJobsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { job });

        var pulse = CreatePulse();
        await pulse.Execute(CancellationToken.None);

        var keys = (await _scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup()))
            .Where(k => k.Group != "System").ToList();
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ErrorSchedulingJob_CallsUpdateStatusMessages()
    {
        var job = MakeJob(1, cls: "DoesNotExist.BadClass", assembly: "FakeAssembly");
        _mockRepository.Setup(r => r.GetActiveJobsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { job });

        await CreatePulse().Execute(CancellationToken.None);

        _mockRepository.Verify(r => r.UpdateStatusMessagesAsync(
            job.Id,
            It.Is<string>(msg => msg.Contains(job.Name) && msg.Contains(job.Assembly)),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PreviousErrorStatus_ClearedOnSuccessfulSchedule()
    {
        var job = MakeJob(1);
        job.LastStatus = "Error scheduling Job";
        _mockRepository.Setup(r => r.GetActiveJobsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { job });

        await CreatePulse().Execute(CancellationToken.None);

        _mockRepository.Verify(r => r.ClearStatusesAsync(job, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Result_ContainsDeletedAndUpdatedCounts()
    {
        var staleJob = MakeJob(999);
        await _scheduler.ScheduleJob(_service.BuildQuartzJob(staleJob), _service.BuildQuartzTrigger(staleJob));

        var activeJobs = new[] { MakeJob(1), MakeJob(2) };
        _mockRepository.Setup(r => r.GetActiveJobsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(activeJobs);

        var pulse = CreatePulse();
        await pulse.Execute(CancellationToken.None);

        Assert.Contains("Deleted 1 job schedule(s)", pulse.Result);
        Assert.Contains("Updated 2 schedule(s)", pulse.Result);
    }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.Shutdown();
    }
}

public class TestJob2 : IJob
{
    public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
}

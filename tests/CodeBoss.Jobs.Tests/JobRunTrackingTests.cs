using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Model;
using Moq;
using Quartz;

namespace CodeBoss.Jobs.Tests;

public class JobRunTrackingTests
{
    // ── Exception.Unwrap ─────────────────────────────────────────────────────

    [Fact]
    public void Unwrap_DrillsThroughNestedSchedulerExceptions()
    {
        var real = new InvalidOperationException("the real cause");
        var wrapped = new SchedulerException("outer", new SchedulerException("inner", real));

        Assert.Same(real, wrapped.Unwrap());
    }

    [Fact]
    public void Unwrap_CollapsesASingleItemAggregate()
    {
        var real = new InvalidOperationException("the real cause");
        var wrapped = new SchedulerException("outer", new AggregateException(real));

        Assert.Same(real, wrapped.Unwrap());
    }

    [Fact]
    public void Unwrap_KeepsAMultiItemAggregateIntact()
    {
        var aggregate = new AggregateException(new Exception("one"), new Exception("two"));

        // Collapsing would lose the other failures.
        Assert.Same(aggregate, aggregate.Unwrap());
    }

    [Fact]
    public void Unwrap_HandlesNull()
    {
        Assert.Null(((Exception)null).Unwrap());
    }

    // ── GetAttempt ───────────────────────────────────────────────────────────

    private static IJobExecutionContext ContextWith(JobDataMap merged, string group = "default", string description = "7")
    {
        var detail = new Mock<IJobDetail>();
        detail.Setup(d => d.Key).Returns(new JobKey("j", group));
        detail.Setup(d => d.Description).Returns(description);
        detail.Setup(d => d.JobDataMap).Returns(new JobDataMap());

        var context = new Mock<IJobExecutionContext>();
        context.Setup(c => c.JobDetail).Returns(detail.Object);
        context.Setup(c => c.MergedJobDataMap).Returns(merged);
        return context.Object;
    }

    [Fact]
    public void GetAttempt_AbsentKey_ReturnsOne()
    {
        // Regression: JobDataMap.GetString THROWS KeyNotFoundException for a missing key rather than
        // returning null. An unguarded read here threw inside the job listener, and Quartz responds
        // to a throwing listener by NOT RUNNING THE JOB at all.
        Assert.Equal(1, ContextWith(new JobDataMap()).GetAttempt());
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("1", 1)]
    [InlineData("not-a-number", 1)]
    [InlineData("0", 1)]
    [InlineData("-2", 1)]
    public void GetAttempt_ReadsTheMergedMap(string raw, int expected)
    {
        var map = new JobDataMap { ["Attempt"] = raw };

        Assert.Equal(expected, ContextWith(map).GetAttempt());
    }

    // ── GetJobRef ────────────────────────────────────────────────────────────

    [Fact]
    public void GetJobRef_SystemGroup_ReturnsNull()
    {
        // The pulse has no ServiceJob row behind it.
        Assert.Null(ContextWith(new JobDataMap(), group: "System").GetJobRef());
    }

    [Fact]
    public void GetJobRef_ReadsTheJobIdFromTheDescription()
    {
        var job = ContextWith(new JobDataMap()).GetJobRef();

        Assert.NotNull(job);
        Assert.Equal(7, job!.Value.Id);
        Assert.Null(job.Value.TenantId);
    }

    // ── Default interface implementations ────────────────────────────────────

    [Fact]
    public async Task MarkRunStarted_DefaultImplementation_WritesRunningStatus()
    {
        var repository = new Mock<IServiceJobRepository> { CallBase = true };
        IServiceJobRepository sut = repository.Object;

        await sut.MarkRunStartedAsync(
            new JobRunStarted(new JobRef(5, 9), "fire-1", "node-1", 1, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)));

        repository.Verify(r => r.SetStatusAsync(
            new JobRef(5, 9), JobRunStatus.Running, It.Is<string>(m => m.Contains("Started at")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkRunCompleted_DefaultImplementation_WritesTheOutcomeStatus()
    {
        var repository = new Mock<IServiceJobRepository> { CallBase = true };
        IServiceJobRepository sut = repository.Object;

        await sut.MarkRunCompletedAsync(new JobRunCompleted(
            new JobRef(5, null), "fire-1", 1, JobRunStatus.Failed, "it broke", TimeSpan.FromSeconds(2), DateTime.UtcNow));

        repository.Verify(r => r.SetStatusAsync(
            new JobRef(5, null), JobRunStatus.Failed, "it broke", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(JobRunStatus.Succeeded)]
    [InlineData(JobRunStatus.Failed)]
    [InlineData(JobRunStatus.TimedOut)]
    [InlineData(JobRunStatus.SchedulingError)]
    [InlineData(JobRunStatus.Retrying)]
    [InlineData(JobRunStatus.Vetoed)]
    [InlineData(JobRunStatus.Running)]
    [InlineData(JobRunStatus.Scheduled)]
    [InlineData(JobRunStatus.None)]
    public void EveryStatusName_FitsTheStatusColumn(JobRunStatus status)
    {
        // ServiceJob.LastStatus and ServiceJobHistory.Status are both MaxLength(50).
        Assert.True(status.ToString().Length <= 50);
    }
}

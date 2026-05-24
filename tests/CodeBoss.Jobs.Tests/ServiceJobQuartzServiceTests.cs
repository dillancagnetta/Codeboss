using CodeBoss.AspNetCore.CbDateTime;
using CodeBoss.Jobs.Model;
using CodeBoss.Jobs.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quartz;

namespace CodeBoss.Jobs.Tests;

public class ServiceJobQuartzServiceTests
{
    private static ServiceJobQuartzService MakeService(MisfirePolicy policy = MisfirePolicy.DoNothing)
    {
        var dateOptions = Options.Create(new DateTimeOptions { TimeZone = "South Africa Standard Time" });
        var jobOptions = Options.Create(new CodeBossJobsOptions { MisfirePolicy = policy });
        return new ServiceJobQuartzService(
            new CodeBossDateTimeProvider(dateOptions, new NullLogger<CodeBossDateTimeProvider>()),
            jobOptions);
    }

    private static ServiceJob MakeJob(int id = 1, string? cron = null, Dictionary<string, string>? parameters = null)
        => new()
        {
            Id = id,
            JobKey = Guid.NewGuid(),
            Name = $"Job_{id}",
            CronExpression = cron ?? "0 */5 * * * ?",
            Class = "CodeBoss.Jobs.Tests.TestJob",
            Assembly = "CodeBoss.Jobs.Tests",
            IsActive = true,
            JobParameters = parameters
        };

    // --- GetJobKey ---

    [Fact]
    public void GetJobKey_WithTenantId_UsesCorrectGroupAndName()
    {
        var svc = MakeService();
        var job = MakeJob();

        var key = svc.GetJobKey(job, 42);

        Assert.Equal($"{job.JobKey}_42", key.Name);
        Assert.Equal("tenant_42", key.Group);
    }

    [Fact]
    public void GetJobKey_WithoutTenantId_UsesDefaultGroup()
    {
        var svc = MakeService();
        var job = MakeJob();

        var key = svc.GetJobKey(job, null);

        Assert.Equal(job.JobKey.ToString(), key.Name);
        Assert.Equal("default", key.Group);
    }

    // --- BuildQuartzJob (single-tenant) ---

    [Fact]
    public void BuildQuartzJob_ValidType_ReturnsJobDetailWithCorrectKey()
    {
        var svc = MakeService();
        var job = MakeJob();

        var detail = svc.BuildQuartzJob(job);

        Assert.NotNull(detail);
        Assert.Equal(job.JobKey.ToString(), detail.Key.Name);
        Assert.Equal(job.Name, detail.Key.Group);
        Assert.Equal(typeof(TestJob), detail.JobType);
        Assert.Equal(job.Id.ToString(), detail.Description);
    }

    [Fact]
    public void BuildQuartzJob_BadType_ReturnsNull()
    {
        var svc = MakeService();
        var job = MakeJob();
        job.Class = "NoSuch.Class";
        job.Assembly = "FakeAssembly";

        var detail = svc.BuildQuartzJob(job);

        Assert.Null(detail);
    }

    [Fact]
    public void BuildQuartzJob_WithParameters_PopulatesJobDataMap()
    {
        var svc = MakeService();
        var job = MakeJob(parameters: new Dictionary<string, string> { ["Key1"] = "Value1" });

        var detail = svc.BuildQuartzJob(job);

        Assert.NotNull(detail);
        Assert.Equal("Value1", detail!.JobDataMap.GetString("Key1"));
    }

    // --- BuildQuartzJob (multi-tenant) ---

    [Fact]
    public void BuildQuartzJob_WithTenantId_AddsTenantIdToDataMap()
    {
        var svc = MakeService();
        var job = MakeJob();

        var detail = svc.BuildQuartzJob(job, 7);

        Assert.NotNull(detail);
        Assert.Equal(7, detail!.JobDataMap.GetIntValue("TenantId"));
        Assert.Equal($"{job.JobKey}_7", detail.Key.Name);
        Assert.Equal("tenant_7", detail.Key.Group);
    }

    [Fact]
    public void BuildQuartzJob_WithoutTenantId_NoTenantIdInDataMap()
    {
        var svc = MakeService();
        var job = MakeJob();

        var detail = svc.BuildQuartzJob(job, null);

        Assert.NotNull(detail);
        Assert.False(detail!.JobDataMap.ContainsKey("TenantId"));
    }

    // --- BuildJobTrigger ---

    [Fact]
    public void BuildJobTrigger_ValidCron_UsesCronExpression()
    {
        var svc = MakeService();
        var job = MakeJob(cron: "0 0 6 * * ?");

        var trigger = svc.BuildJobTrigger(job, 1) as ICronTrigger;

        Assert.NotNull(trigger);
        Assert.Equal("0 0 6 * * ?", trigger!.CronExpressionString);
    }

    [Fact]
    public void BuildJobTrigger_InvalidCron_FallsBackToNeverScheduled()
    {
        var svc = MakeService();
        var job = MakeJob(cron: "NOT_A_CRON");

        var trigger = svc.BuildJobTrigger(job, 1) as ICronTrigger;

        Assert.NotNull(trigger);
        Assert.Equal(ServiceJob.NeverScheduledCronExpression, trigger!.CronExpressionString);
    }

    [Fact]
    public void BuildJobTrigger_TriggerKey_MatchesJobKey()
    {
        var svc = MakeService();
        var job = MakeJob();

        var jobKey = svc.GetJobKey(job, 3);
        var trigger = svc.BuildJobTrigger(job, 3);

        Assert.Equal($"{jobKey.Name}_trigger", trigger.Key.Name);
        Assert.Equal(jobKey.Group, trigger.Key.Group);
    }

    // --- MisfirePolicy ---

    [Theory]
    [InlineData(MisfirePolicy.DoNothing)]
    [InlineData(MisfirePolicy.FireAndProceed)]
    [InlineData(MisfirePolicy.IgnoreMisfires)]
    public void BuildJobTrigger_AllMisfirePolicies_BuildsWithoutException(MisfirePolicy policy)
    {
        var svc = MakeService(policy);
        var job = MakeJob();

        var trigger = svc.BuildJobTrigger(job, 1);

        Assert.NotNull(trigger);
    }

    [Theory]
    [InlineData(MisfirePolicy.DoNothing)]
    [InlineData(MisfirePolicy.FireAndProceed)]
    [InlineData(MisfirePolicy.IgnoreMisfires)]
    public void BuildQuartzTrigger_AllMisfirePolicies_BuildsWithoutException(MisfirePolicy policy)
    {
        var svc = MakeService(policy);
        var job = MakeJob();

        var trigger = svc.BuildQuartzTrigger(job);

        Assert.NotNull(trigger);
    }
}

using CodeBoss.AspNetCore.CbDateTime;
using CodeBoss.Jobs.Jobs;
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

    // --- JobDataMap is string-only (persistent store with useProperties=true) ---

    [Fact]
    public void BuildQuartzJob_TenantId_IsStoredAsString()
    {
        var svc = MakeService();
        var job = MakeJob();

        var detail = svc.BuildQuartzJob(job, 7);

        // A persistent store with quartz.jobStore.useProperties=true throws on any non-string value.
        Assert.IsType<string>(detail!.JobDataMap["TenantId"]);
        Assert.Equal("7", detail.JobDataMap.GetString("TenantId"));
        // ...and it still reads back as an int for GetTenantIdFromQuartz.
        Assert.Equal(7, detail.JobDataMap.GetIntValue("TenantId"));
    }

    [Fact]
    public void BuildQuartzJob_AllJobDataMapValues_AreStrings()
    {
        var svc = MakeService();
        var job = MakeJob(parameters: new Dictionary<string, string> { ["Key1"] = "Value1", ["Key2"] = null! });

        var detail = svc.BuildQuartzJob(job, 3);

        Assert.All(detail!.JobDataMap.Values, v => Assert.IsType<string>(v));
        // A null parameter stays present as empty rather than disappearing from the map.
        Assert.True(detail.JobDataMap.ContainsKey("Key2"));
        Assert.Equal(string.Empty, detail.JobDataMap.GetString("Key2"));
    }

    // --- Per-job misfire override ---

    [Theory]
    [InlineData("FireAndProceed", MisfireInstruction.CronTrigger.FireOnceNow)]
    [InlineData("fireandproceed", MisfireInstruction.CronTrigger.FireOnceNow)]
    [InlineData("IgnoreMisfires", MisfireInstruction.IgnoreMisfirePolicy)]
    public void BuildJobTrigger_PerJobMisfirePolicy_OverridesGlobalOption(string policy, int expected)
    {
        var svc = MakeService(MisfirePolicy.DoNothing);
        var job = MakeJob(parameters: new Dictionary<string, string> { ["MisfirePolicy"] = policy });

        var trigger = svc.BuildJobTrigger(job, 1);

        Assert.Equal(expected, trigger.MisfireInstruction);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    public void BuildJobTrigger_UnparseableMisfirePolicy_FallsBackToGlobal(string policy)
    {
        var svc = MakeService(MisfirePolicy.IgnoreMisfires);
        var job = MakeJob(parameters: new Dictionary<string, string> { ["MisfirePolicy"] = policy });

        var trigger = svc.BuildJobTrigger(job, 1);

        Assert.Equal(MisfireInstruction.IgnoreMisfirePolicy, trigger.MisfireInstruction);
    }

    // --- Extensibility: hooks are overridable rather than hidden with `new` ---

    [Fact]
    public void Subclass_CanOverrideTimeZone()
    {
        var svc = new UtcPinnedService();
        var job = MakeJob(cron: "0 0 6 * * ?");

        var trigger = (ICronTrigger)svc.BuildJobTrigger(job, 1);

        Assert.Equal(TimeZoneInfo.Utc, trigger.TimeZone);
    }

    private sealed class UtcPinnedService() : ServiceJobQuartzService(
        new CodeBossDateTimeProvider(
            Options.Create(new DateTimeOptions { TimeZone = "South Africa Standard Time" }),
            new NullLogger<CodeBossDateTimeProvider>()))
    {
        protected override TimeZoneInfo ResolveTimeZone(ServiceJob job) => TimeZoneInfo.Utc;
    }
}

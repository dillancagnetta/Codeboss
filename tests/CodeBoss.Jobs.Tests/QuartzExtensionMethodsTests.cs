using CodeBoss.Jobs.Model;
using Moq;
using Quartz;

namespace CodeBoss.Jobs.Tests;

public class QuartzExtensionMethodsTests
{
    // --- IsValidCronDescription ---

    [Theory]
    [InlineData("0 */5 * * * ?", true)]
    [InlineData("0 0 12 * * ?", true)]
    [InlineData("0 0 0 1 1 ? 2099", true)]
    [InlineData("INVALID_CRON", false)]
    [InlineData("", false)]
    [InlineData("* * * * *", false)] // 5-field Unix cron, invalid for Quartz (needs 6/7 fields)
    public void IsValidCronDescription_ReturnsExpected(string cron, bool expected)
    {
        Assert.Equal(expected, cron.IsValidCronDescription());
    }

    // --- GetCompiledType ---

    [Fact]
    public void GetCompiledType_ValidClassAndAssembly_ReturnsType()
    {
        var job = new ServiceJob
        {
            Class = "CodeBoss.Jobs.Tests.TestJob",
            Assembly = "CodeBoss.Jobs.Tests"
        };

        var type = job.GetCompiledType();

        Assert.NotNull(type);
        Assert.Equal(typeof(TestJob), type);
    }

    [Fact]
    public void GetCompiledType_BadClass_ReturnsNull()
    {
        var job = new ServiceJob
        {
            Class = "No.Such.Class",
            Assembly = "FakeAssembly"
        };

        var type = job.GetCompiledType();

        Assert.Null(type);
    }

    [Fact]
    public void GetCompiledType_NullAssembly_ReturnsNull()
    {
        var job = new ServiceJob
        {
            Class = "CodeBoss.Jobs.Tests.TestJob",
            Assembly = null!
        };

        var type = job.GetCompiledType();

        // Type.GetType("Class, ") returns null when assembly segment is empty/null
        Assert.Null(type);
    }

    // --- GetJobIdFromQuartz ---

    [Fact]
    public void GetJobIdFromQuartz_ReturnsIdFromDescription()
    {
        var mockDetail = new Mock<IJobDetail>();
        mockDetail.Setup(d => d.Description).Returns("42");

        var mockContext = new Mock<IJobExecutionContext>();
        mockContext.Setup(c => c.JobDetail).Returns(mockDetail.Object);

        var id = mockContext.Object.GetJobIdFromQuartz();

        Assert.Equal(42, id);
    }

    // --- GetTenantIdFromQuartz ---

    [Fact]
    public void GetTenantIdFromQuartz_WhenTenantIdPresent_ReturnsValue()
    {
        var dataMap = new JobDataMap();
        dataMap["TenantId"] = 7;

        var mockDetail = new Mock<IJobDetail>();
        mockDetail.Setup(d => d.JobDataMap).Returns(dataMap);

        var mockContext = new Mock<IJobExecutionContext>();
        mockContext.Setup(c => c.JobDetail).Returns(mockDetail.Object);

        var tenantId = mockContext.Object.GetTenantIdFromQuartz();

        Assert.Equal(7, tenantId);
    }

    [Fact]
    public void GetTenantIdFromQuartz_WhenAbsent_ReturnsNull()
    {
        var dataMap = new JobDataMap();

        var mockDetail = new Mock<IJobDetail>();
        mockDetail.Setup(d => d.JobDataMap).Returns(dataMap);

        var mockContext = new Mock<IJobExecutionContext>();
        mockContext.Setup(c => c.JobDetail).Returns(mockDetail.Object);

        var tenantId = mockContext.Object.GetTenantIdFromQuartz();

        Assert.Null(tenantId);
    }
}

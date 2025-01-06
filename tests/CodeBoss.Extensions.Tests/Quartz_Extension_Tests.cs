using CodeBoss.Jobs;
using CodeBoss.Jobs.Model;

namespace CodeBoss.Extensions.Tests;

public class Quartz_Extension_Tests
{
    [Fact]
    public void GetCompiledType_WithValidServiceJob_ReturnsCorrectType()
    {
        // Arrange
        var serviceJob = new ServiceJob
        {
            Class = typeof(TestJob).FullName,
            Assembly = typeof(TestJob).Assembly.FullName
        };

        // Act
        var result = serviceJob.GetCompiledType();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(typeof(TestJob), result);
    }
}

// Helper class for testing
public class TestJob { }
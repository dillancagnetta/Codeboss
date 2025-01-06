namespace CodeBoss.Extensions.Tests;

public class Type_Extensions_Tests
{
    [Fact]
    public void GetGenericArgumentsOfBaseType_WithValidGenericType_ReturnsCorrectArguments()
    {
        // Arrange
        var type = typeof(TestDerivedClass);
        var genericType = typeof(TestBaseClass<,>);

        // Act
        var result = type.GetGenericArgumentsOfBaseType(genericType);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal(typeof(int), result[0]);
        Assert.Equal(typeof(string), result[1]);
    }

    [Fact]
    public void GetGenericArgumentsOfBaseType_WithNonGenericType_ThrowsArgumentException()
    {
        // Arrange
        var type = typeof(TestDerivedClass);
        var nonGenericType = typeof(object);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => type.GetGenericArgumentsOfBaseType(nonGenericType));
        Assert.Equal("Must be a generic type definition.", exception.Message);
        Assert.Equal("genericType", exception.ParamName);
    }

    [Fact]
    public void GetGenericArgumentsOfBaseType_WithNonDescendantType_ThrowsArgumentException()
    {
        // Arrange
        var type = typeof(TestDerivedClass);
        var unrelatedGenericType = typeof(List<>);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => type.GetGenericArgumentsOfBaseType(unrelatedGenericType));
        Assert.Equal("Type was not a descendend.", exception.Message);
        Assert.Equal("genericType", exception.ParamName);
    }
    
    // Helper classes for testing
    private class TestBaseClass<T1, T2> { }
    private class TestDerivedClass : TestBaseClass<int, string> { }
}
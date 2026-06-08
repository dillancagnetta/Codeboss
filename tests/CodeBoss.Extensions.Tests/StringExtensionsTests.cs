using System;
using System.Collections.Generic;
using System.Linq;
using CodeBoss.Extensions;

namespace CodeBoss.Extensions.Tests;

public class StringExtensionsTests
{
    #region Parsing

    [Theory]
    [InlineData("123", 123)]
    [InlineData("-5", -5)]
    [InlineData("abc", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void AsInteger_ParsesOrZero(string? input, int expected)
        => Assert.Equal(expected, input.AsInteger());

    [Fact]
    public void AsIntegerOrNull_Invalid_ReturnsNull()
        => Assert.Null("nope".AsIntegerOrNull());

    [Fact]
    public void AsIntegerOrNull_Valid_ReturnsValue()
        => Assert.Equal(42, "42".AsIntegerOrNull());

    [Fact]
    public void AsGuid_Valid_ReturnsGuid()
    {
        var g = Guid.NewGuid();
        Assert.Equal(g, g.ToString().AsGuid());
    }

    [Fact]
    public void AsGuid_Invalid_ReturnsEmpty()
        => Assert.Equal(Guid.Empty, "not-a-guid".AsGuid());

    [Fact]
    public void AsGuidOrNull_Invalid_ReturnsNull()
        => Assert.Null("not-a-guid".AsGuidOrNull());

    #endregion

    #region Email

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("first.last@sub.domain.org")]
    public void IsValidEmail_Valid_True(string email)
        => Assert.True(email.IsValidEmail());

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("@missinglocal.com")]
    [InlineData("missingat.com")]
    [InlineData("")]
    [InlineData(null)]
    public void IsValidEmail_Invalid_False(string? email)
        => Assert.False(email!.IsValidEmail());

    #endregion

    #region Ensure starts/ends

    [Fact]
    public void EnsureEndsWith_Absent_Appends()
        => Assert.Equal("path/", "path".EnsureEndsWith('/'));

    [Fact]
    public void EnsureEndsWith_Present_Unchanged()
        => Assert.Equal("path/", "path/".EnsureEndsWith('/'));

    [Fact]
    public void EnsureStartsWith_Absent_Prepends()
        => Assert.Equal("/path", "path".EnsureStartsWith('/'));

    [Fact]
    public void EnsureStartsWith_Present_Unchanged()
        => Assert.Equal("/path", "/path".EnsureStartsWith('/'));

    #endregion

    #region SplitCase

    [Theory]
    [InlineData("PascalCase", "Pascal Case")]
    [InlineData("camelCaseWord", "camel Case Word")]
    [InlineData("XMLHttpRequest", "XML Http Request")]
    public void SplitCase_SeparatesWords(string input, string expected)
        => Assert.Equal(expected, input.SplitCase());

    [Fact]
    public void SplitCase_Null_ReturnsNull()
        => Assert.Null(((string?)null).SplitCase());

    #endregion

    #region JoinStringsWithCommaAnd

    [Fact]
    public void Join_Empty_ReturnsEmpty()
        => Assert.Equal(string.Empty, Enumerable.Empty<string>().JoinStringsWithCommaAnd());

    [Fact]
    public void Join_Single_ReturnsItem()
        => Assert.Equal("apple", new[] { "apple" }.JoinStringsWithCommaAnd());

    [Fact]
    public void Join_Many_CommasPlusAnd()
        => Assert.Equal("apple, banana and cherry",
            new[] { "apple", "banana", "cherry" }.JoinStringsWithCommaAnd());

    #endregion

    #region Null/empty + ToGuids

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" ", false)]
    [InlineData("x", false)]
    public void IsNullOrEmpty_Cases(string? input, bool expected)
        => Assert.Equal(expected, input!.IsNullOrEmpty());

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("x", false)]
    public void IsNullOrWhiteSpace_Cases(string? input, bool expected)
        => Assert.Equal(expected, input!.IsNullOrWhiteSpace());

    [Fact]
    public void ToGuids_ConvertsAll()
    {
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var result = new[] { g1.ToString(), g2.ToString() }.ToGuids().ToList();
        Assert.Equal(new[] { g1, g2 }, result);
    }

    [Fact]
    public void ToGuids_Empty_ReturnsEmpty()
        => Assert.Empty(Enumerable.Empty<string>().ToGuids());

    #endregion
}

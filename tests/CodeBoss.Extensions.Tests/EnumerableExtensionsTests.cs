using System;
using System.Collections.Generic;
using System.Linq;
using CodeBoss.Extensions;

namespace CodeBoss.Extensions.Tests;

public class EnumerableExtensionsTests
{
    #region AddRangeDistinct

    [Fact]
    public void AddRangeDistinct_List_MutatesInPlaceAndSkipsDuplicates()
    {
        var source = new List<int> { 1, 2, 3 };

        var result = source.AddRangeDistinct(new[] { 3, 4, 5 });

        Assert.Same(source, result);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, source);
    }

    [Fact]
    public void AddRangeDistinct_Array_ReturnsNewListWithoutThrowing()
    {
        int[] source = { 1, 2, 3 };

        var result = source.AddRangeDistinct(new[] { 3, 4 });

        Assert.Equal(new[] { 1, 2, 3, 4 }, result);
        Assert.Equal(new[] { 1, 2, 3 }, source);
    }

    [Fact]
    public void AddRangeDistinct_LinqSequence_ReturnsNewListWithoutThrowing()
    {
        var source = Enumerable.Range(1, 3).Where(x => x > 0);

        var result = source.AddRangeDistinct(new[] { 2, 4 });

        Assert.Equal(new[] { 1, 2, 3, 4 }, result);
    }

    [Fact]
    public void AddRangeDistinct_NullSource_ReturnsItems()
    {
        IEnumerable<string> source = null;

        var result = source.AddRangeDistinct(new[] { "a", "b" });

        Assert.Equal(new[] { "a", "b" }, result);
    }

    [Fact]
    public void AddRangeDistinct_EmptyItems_ReturnsSourceUnchanged()
    {
        var source = new List<int> { 1, 2 };

        var result = source.AddRangeDistinct(Array.Empty<int>());

        Assert.Same(source, result);
        Assert.Equal(new[] { 1, 2 }, source);
    }

    [Fact]
    public void AddRangeDistinct_WithComparer_UsesComparer()
    {
        var source = new List<string> { "a", "B" };

        var result = source.AddRangeDistinct(new[] { "A", "b", "c" }, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(new[] { "a", "B", "c" }, result);
    }

    #endregion
}

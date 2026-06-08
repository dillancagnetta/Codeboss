using System;
using System.Collections.Generic;
using System.Linq;
using Codeboss.Types;
using CodeBoss.Extensions;

namespace CodeBoss.Extensions.Tests;

public class DateTimeExtensionsTests
{
    // Fixed reference "now" for deterministic relative/elapsed assertions.
    private static readonly DateTime Now = new(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);
    private static IDateTimeProvider Provider => new FakeDateTimeProvider(Now);

    #region ToRelativeDateString

    [Fact]
    public void RelativeDateString_LessThan5Seconds_JustNow()
    {
        var dt = Now.AddSeconds(-2);
        Assert.Equal("just now", dt.ToRelativeDateString(Provider));
    }

    [Theory]
    [InlineData(-30, "30 seconds ago")]
    [InlineData(-90, "1 minute ago")]
    [InlineData(-300, "5 minutes ago")]
    public void RelativeDateString_Past_SecondsAndMinutes(int seconds, string expected)
    {
        var dt = Now.AddSeconds(seconds);
        Assert.Equal(expected, dt.ToRelativeDateString(Provider));
    }

    [Fact]
    public void RelativeDateString_OneHour_AboutAnHour()
    {
        var dt = Now.AddMinutes(-65);
        Assert.Equal("about an hour ago", dt.ToRelativeDateString(Provider));
    }

    [Theory]
    [InlineData(-5, "5 hours ago")]
    [InlineData(-25, "1 day ago")]
    [InlineData(-72, "3 days ago")]
    public void RelativeDateString_Past_HoursAndDays(int hours, string expected)
    {
        var dt = Now.AddHours(hours);
        Assert.Equal(expected, dt.ToRelativeDateString(Provider));
    }

    [Theory]
    [InlineData(-7, "1 week ago")]
    [InlineData(-21, "3 weeks ago")]
    public void RelativeDateString_Past_Weeks(int days, string expected)
    {
        var dt = Now.AddDays(days);
        Assert.Equal(expected, dt.ToRelativeDateString(Provider));
    }

    [Fact]
    public void RelativeDateString_Months_FormatsMonths()
    {
        var dt = Now.AddDays(-120); // ~4 months
        Assert.Equal("4 months ago", dt.ToRelativeDateString(Provider));
    }

    [Fact]
    public void RelativeDateString_Years_FormatsYears()
    {
        var dt = Now.AddDays(-365 * 3); // ~3 years
        Assert.Equal("3 years ago", dt.ToRelativeDateString(Provider));
    }

    [Fact]
    public void RelativeDateString_Future_UsesInPrefix()
    {
        var dt = Now.AddMinutes(5);
        Assert.Equal("in 5 minutes", dt.ToRelativeDateString(Provider));
    }

    [Fact]
    public void RelativeDateString_MaxDaysExceeded_SameYear_LongFormatNoYear()
    {
        var dt = Now.AddDays(-40);
        var result = dt.ToRelativeDateString(Provider, maxDays: 30);
        Assert.Equal(dt.ToString(@"MMMM d a\t h:mm tt"), result);
    }

    [Fact]
    public void RelativeDateString_MaxDaysExceeded_DifferentYear_LongFormatWithYear()
    {
        var dt = Now.AddDays(-400);
        var result = dt.ToRelativeDateString(Provider, maxDays: 30);
        Assert.Equal(dt.ToString(@"MMMM d, yyyy a\t h:mm tt"), result);
    }

    [Fact]
    public void RelativeDateStringUTC_Null_ReturnsEmpty()
    {
        DateTime? dt = null;
        Assert.Equal(string.Empty, dt.ToRelativeDateStringUTC());
    }

    #endregion

    #region ToElapsedString

    [Fact]
    public void ElapsedString_Past_AppendsAgo()
    {
        var dt = Now.AddHours(-3);
        Assert.Equal("3 Hours Ago", dt.ToElapsedString(Provider));
    }

    [Fact]
    public void ElapsedString_Future_AppendsFromNow()
    {
        var dt = Now.AddHours(3);
        Assert.Equal("3 Hours From Now", dt.ToElapsedString(Provider));
    }

    [Fact]
    public void ElapsedString_Condensed_NoDirectionSuffix()
    {
        var dt = Now.AddHours(-3);
        Assert.Equal("3hr", dt.ToElapsedString(Provider, condensed: true));
    }

    [Fact]
    public void ElapsedString_Days_WhenIncludeTimeFalse()
    {
        var dt = Now.AddDays(-5);
        Assert.Equal("5 Days Ago", dt.ToElapsedString(Provider, includeTime: false));
    }

    [Fact]
    public void ElapsedString_NullableNull_ReturnsEmpty()
    {
        DateTime? dt = null;
        Assert.Equal(string.Empty, dt.ToElapsedString(Provider));
    }

    #endregion

    #region Age / Year diffs

    [Fact]
    public void Age_BirthdayPassed_FullYears()
    {
        var birth = Now.AddYears(-30).AddDays(-10);
        Assert.Equal(30, birth.Age(Provider));
    }

    [Fact]
    public void Age_BirthdayNotYetPassed_SubtractsOne()
    {
        var birth = Now.AddYears(-30).AddDays(10);
        Assert.Equal(29, birth.Age(Provider));
    }

    [Fact]
    public void Age_NullableNull_ReturnsZero()
    {
        DateTime? birth = null;
        Assert.Equal(0, birth.Age());
    }

    [Fact]
    public void GetDifferenceInYears_Computes()
    {
        var start = new DateTime(2000, 1, 1);
        var end = new DateTime(2026, 1, 1);
        Assert.Equal(26, start.GetDifferenceInYears(end));
    }

    #endregion

    #region Boundaries

    [Fact]
    public void StartOfMonth_FirstDayMidnight()
    {
        var dt = new DateTime(2026, 3, 23, 11, 15, 0);
        Assert.Equal(new DateTime(2026, 3, 1), dt.StartOfMonth());
    }

    [Fact]
    public void EndOfMonth_LastDay()
    {
        var dt = new DateTime(2026, 3, 23, 11, 15, 0);
        Assert.Equal(new DateTime(2026, 3, 31), dt.EndOfMonth());
    }

    [Fact]
    public void StartOfWeek_Sunday()
    {
        var wed = new DateTime(2026, 6, 10); // Wednesday
        Assert.Equal(new DateTime(2026, 6, 7), wed.StartOfWeek(DayOfWeek.Sunday));
    }

    [Fact]
    public void EndOfWeek_SixDaysAfterStart()
    {
        var wed = new DateTime(2026, 6, 10);
        Assert.Equal(new DateTime(2026, 6, 13), wed.EndOfWeek(DayOfWeek.Sunday));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(12, 4)]
    public void Quarter_ReturnsCalendarQuarter(int month, int expected)
    {
        var dt = new DateTime(2026, month, 15);
        Assert.Equal(expected, dt.Quarter());
    }

    #endregion

    #region GetWeeklyDatesFrom

    [Fact]
    public void GetWeeklyDates_AllOnTargetDay_AndSpaced7Days()
    {
        var start = new DateTime(2020, 1, 1); // Wednesday, in past so all yield
        List<DateTime> dates = start.GetWeeklyDatesFrom(DayOfWeek.Sunday, weeks: 4).ToList();

        Assert.Equal(4, dates.Count);
        Assert.All(dates, d => Assert.Equal(DayOfWeek.Sunday, d.DayOfWeek));
        for (int i = 1; i < dates.Count; i++)
            Assert.Equal(7, (dates[i] - dates[i - 1]).TotalDays);
    }

    [Fact]
    public void GetWeeklyDates_StopsAtToday()
    {
        var start = DateTime.Today.AddDays(-3);
        var dates = start.GetWeeklyDatesFrom(start.DayOfWeek, weeks: 52).ToList();
        Assert.All(dates, d => Assert.True(d <= DateTime.Today));
    }

    #endregion
}

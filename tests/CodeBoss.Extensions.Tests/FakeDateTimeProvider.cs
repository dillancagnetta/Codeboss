using System;
using Codeboss.Types;

namespace CodeBoss.Extensions.Tests;

/// <summary>
/// Deterministic <see cref="IDateTimeProvider"/> for tests. <see cref="Now"/> is fixed at construction.
/// </summary>
public sealed class FakeDateTimeProvider : IDateTimeProvider
{
    public FakeDateTimeProvider(DateTime now) => Now = now;

    public TimeZoneInfo TimeZoneInfo => TimeZoneInfo.Utc;
    public DateTime Now { get; }

    public DateTime ConvertLocalDateTimeToProviderDateTime(DateTime localDateTime) => localDateTime;
    public DateTime ConvertFromUtc(DateTime utcDateTime) => utcDateTime;

    public DateTime SundayDate(DateTime inputDate, DayOfWeek firstDayOfWeek)
        => inputDate.Date.AddDays(((int)DayOfWeek.Sunday - (int)inputDate.DayOfWeek + 7) % 7);
}

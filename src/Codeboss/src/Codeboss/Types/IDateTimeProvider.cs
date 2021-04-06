using System;

namespace Codeboss.Types
{
    public interface IDateTimeProvider
    {
        public TimeZoneInfo TimeZoneInfo { get; }
        public DateTime Now { get; }
        public DateTime ConvertLocalDateTimeToProviderDateTime(DateTime localDateTime);
        public DateTime ConvertFromUtc(DateTime utcDateTime);
        public DateTime SundayDate(DateTime inputDate, DayOfWeek firstDayOfWeek);
    }
}

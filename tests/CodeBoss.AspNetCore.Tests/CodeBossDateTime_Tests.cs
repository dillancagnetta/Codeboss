using System;
using CodeBoss.AspNetCore.CbDateTime;
using CodeBoss.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodeBoss.AspNetCore.Tests
{
    public class CodeBossDateTime_Tests
    {
        [Fact]
        public void Should_convert_from_Utc_to_timezone_date()
        {
            var options = Options.Create(new DateTimeOptions { TimeZone = "South Africa Standard Time" });
            var dateTimeProvider = new CodeBossDateTimeProvider(options, new NullLogger<CodeBossDateTimeProvider>());

            var utcDateTime = DateTime.UtcNow;
            var timeZoneDateTime = dateTimeProvider.ConvertFromUtc(utcDateTime);

            var utcToTimeZone = utcDateTime.AddHours(+2);
            Assert.Equal(utcToTimeZone, timeZoneDateTime);
        }


        [Fact]
        public void Should_calculate_difference_between_start_and_end_of_weeks()
        {
            var from = DateTime.UtcNow.StartOfWeek(DayOfWeek.Monday);
            var to = DateTime.UtcNow.EndOfWeek(DayOfWeek.Monday);

            Assert.True(from.DayOfWeek == DayOfWeek.Monday);
            Assert.True(to.DayOfWeek == DayOfWeek.Sunday);
        }
        
        [Fact]
        public void Should_generate_weekly_dates()
        {
            DateTime from = DateTime.Today.AddMonths(-1); // Example start date (1 months ago)
            DateTime previous = from;
            foreach (var date in from.GetWeeklyDatesFrom(DayOfWeek.Wednesday))
            {
                Assert.True(date.DayOfWeek == DayOfWeek.Wednesday);
                Assert.True(from < DateTime.UtcNow);
                Assert.True(previous < date);
                
                // Adjust to compare
                previous = date;
            }
        }
    }
}

using System;
using CodeBoss.AspNetCore.CbDateTime;
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
            var options = Options.Create(new DateTimeOptions {TimeZone = "South Africa Standard Time"});
            var dateTimeProvider = new CodeBossDateTimeProvider(options, new NullLogger<CodeBossDateTimeProvider>());

            var utcDateTime = DateTime.UtcNow;
            var timeZoneDateTime = dateTimeProvider.ConvertFromUtc(utcDateTime);

            var utcToTimeZone = utcDateTime.AddHours(+2);
            Assert.Equal(utcToTimeZone, timeZoneDateTime);
        }
    }
}

using System;
using Microsoft.Extensions.Options;
using TimeZoneInfo = System.TimeZoneInfo;

namespace CodeBoss.AspNetCore.CbDateTime
{
    public class CbDateTime
    {
        private readonly DateTimeOptions _options;

        public CbDateTime(IOptions<DateTimeOptions> options) => _options = options?.Value;

        private TimeZoneInfo _timeZoneInfo;
        public TimeZoneInfo TimeZoneInfo
        {
            get
            {
                if (_options?.TimeZone == null)
                {
                    _timeZoneInfo = TimeZoneInfo.Local;
                }
                else
                {
                    _timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(_options?.TimeZone);
                }

                return _timeZoneInfo;
            }
        }

        /// <summary>
        /// Gets current datetime based on the TimeZone setting set in app.settings
        /// </summary>
        /// <value>
        /// A <see cref="System.DateTime"/> that current datetime based on the TimeZone.
        /// </value>
        public DateTime Now => TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo);

        /// <summary>
        /// Converts the local date time to rock date time.
        /// Use this to convert a local datetime (for example, the datetime of a file stored on the server) to the Rock OrgTimeZone
        /// </summary>
        /// <param name="localDateTime">The local date time.</param>
        /// <returns></returns>
        public DateTime ConvertLocalDateTimeToCbDateTime(DateTime localDateTime)
        {
            return TimeZoneInfo.ConvertTime(localDateTime, TimeZoneInfo);
        }

        /// <summary>
        /// Gets the Date of which Sunday is associated with the specified Date/Time, based on what the First Day Of Week is defined as.
        /// </summary>
        /// <param name="inputDate">The date time.</param>
        /// <param name="firstDayOfWeek">The first day of week. Use the override of this method with only the inputDate to assume the system setting.</param>
        /// <returns></returns>
        public DateTime SundayDate(DateTime inputDate, DayOfWeek firstDayOfWeek)
        {
            /*             
             * 
             * I restored the ability to specify the firstDayOfWeek so that each StreakType could have a custom setting. Some streaks, like those
             * for serving, make sense to measure Wed - Tues so that an entire three day "weekend" of services is counted as the same Sunday Date.
             * However, just because the church wants the streak type to have a Wed start of week for the streak type doesn't mean they want the
             * entire system to be configured this way.
             */

            // Get the number of days until the next Sunday date
            int sundayDiff = 7 - (int)inputDate.DayOfWeek;

            // Figure out which DayOfWeek would be the lastDayOfWeek ( which would be the DayOfWeek before the firstDayOfWeek )
            DayOfWeek lastDayOfWeek;

            if(firstDayOfWeek == DayOfWeek.Sunday)
            {
                // The day before Sunday is Saturday
                lastDayOfWeek = DayOfWeek.Saturday;
            }
            else
            {
                // if the startOfWeek isn't Sunday, we can just subtract by 1
                lastDayOfWeek = firstDayOfWeek - 1;
            }

            //// There are 3 cases to deal with, and it can get confusing if Sunday isn't the last day of the week
            //// 1) The input date's DOW is Sunday. Today is the Sunday, so the Sunday Date is today
            //// 2) The input date's DOW is after the Last DOW (Today is Thursday, and the week ends next week on Tuesday).
            //// 3) The input date's DOW is before the Last DOW (Today is Monday, but the week ends this week on Tuesday)
            DateTime sundayDate;

            if(inputDate.DayOfWeek == DayOfWeek.Sunday)
            {
                sundayDate = inputDate;
            }
            else if(lastDayOfWeek < inputDate.DayOfWeek)
            {
                // If the lastDayOfWeek after the current day of week, we can simply add 
                sundayDate = inputDate.AddDays(sundayDiff);
            }
            else
            {
                // If the current DayOfWeek is on or after the lastDayOfWeek, it'll be the *previous* Sunday.
                // For example, if the Last Day of the Week is Monday (10/7/2019),
                // the Sunday Date will *before* the current date (10/6/2019)
                sundayDate = inputDate.AddDays(sundayDiff - 7);
            }

            return sundayDate.Date;
        }
    }
}


using System;
using Codeboss.Types;

namespace CodeBoss.Extensions
{
    public static partial class Extensions
    {
        /// <summary>
        /// Returns the age at the current date.
        /// </summary>
        /// <param name="start"></param>
        /// <returns></returns>
        public static int Age(this DateTime? start)
        {
            if(start.HasValue)
                return start.Age();
            else
                return 0;
        }

        /// <summary>
        /// Returns the age at the current date.
        /// </summary>
        /// <param name="start"></param>
        /// <returns></returns>
        public static int Age(this DateTime start , IDateTimeProvider dateTimeProvider)
        {
            var now = dateTimeProvider.Now.Date;
            int age = now.Year - start.Year;
            if(start > now.AddYears(-age))
            {
                age--;
            }

            return age;
        }

        /// <summary>
        /// The total months.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <returns></returns>
        public static int TotalMonths(this DateTime end, DateTime start)
        {
            return (end.Year * 12 + end.Month) - (start.Year * 12 + start.Month);
        }

        /// <summary>
        /// The total years.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <returns></returns>
        public static int TotalYears(this DateTime end, DateTime start)
        {
            return (end.Year) - (start.Year);
        }

        /// <summary>
        /// Returns a friendly elapsed time string.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        /// <param name="condensed">if set to <c>true</c> [condensed].</param>
        /// <param name="includeTime">if set to <c>true</c> [include time].</param>
        /// <returns></returns>
        public static string ToElapsedString(this DateTime? dateTime, bool condensed = false, bool includeTime = true)
        {
            if(dateTime.HasValue)
            {
                return ToElapsedString(dateTime.Value, condensed, includeTime);
            }
            else
                return string.Empty;
        }

        /// <summary>
        /// Returns a friendly elapsed time string.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        /// <param name="condensed">if set to <c>true</c> [condensed].</param>
        /// <param name="includeTime">if set to <c>true</c> [include time].</param>
        /// <returns></returns>
        public static string ToElapsedString(this DateTime dateTime, IDateTimeProvider dateTimeProvider, bool condensed = false, bool includeTime = true)
        {
            DateTime start = dateTime;
            DateTime end = dateTimeProvider.Now;

            string direction = " Ago";
            TimeSpan timeSpan = end.Subtract(start);
            if(timeSpan.TotalMilliseconds < 0)
            {
                direction = " From Now";
                start = end;
                end = dateTime;
                timeSpan = timeSpan.Negate();
            }

            string duration = "";

            if(timeSpan.TotalHours < 24 && includeTime)
            {
                // Less than one second
                if(timeSpan.TotalSeconds < 2)
                    duration = string.Format("1{0}", condensed ? "sec" : " Second");
                else if(timeSpan.TotalSeconds < 60)
                    duration = string.Format("{0:N0}{1}", Math.Truncate(timeSpan.TotalSeconds), condensed ? "sec" : " Seconds");
                else if(timeSpan.TotalMinutes < 2)
                    duration = string.Format("1{0}", condensed ? "min" : " Minute");
                else if(timeSpan.TotalMinutes < 60)
                    duration = string.Format("{0:N0}{1}", Math.Truncate(timeSpan.TotalMinutes), condensed ? "min" : " Minutes");
                else if(timeSpan.TotalHours < 2)
                    duration = string.Format("1{0}", condensed ? "hr" : " Hour");
                else if(timeSpan.TotalHours < 24)
                    duration = string.Format("{0:N0}{1}", Math.Truncate(timeSpan.TotalHours), condensed ? "hr" : " Hours");
            }

            if(duration == "")
            {
                if(timeSpan.TotalDays < 2)
                    duration = string.Format("1{0}", condensed ? "day" : " Day");
                else if(timeSpan.TotalDays < 31)
                    duration = string.Format("{0:N0}{1}", Math.Truncate(timeSpan.TotalDays), condensed ? "days" : " Days");
                else if(end.TotalMonths(start) <= 1)
                    duration = string.Format("1{0}", condensed ? "mon" : " Month");
                else if(end.TotalMonths(start) <= 18)
                    duration = string.Format("{0:N0}{1}", end.TotalMonths(start), condensed ? "mon" : " Months");
                else if(end.TotalYears(start) <= 1)
                    duration = string.Format("1{0}", condensed ? "yr" : " Year");
                else
                    duration = string.Format("{0:N0}{1}", end.TotalYears(start), condensed ? "yrs" : " Years");
            }

            return duration + (condensed ? "" : direction);
        }

        /// <summary>
        /// Returns a string in FB style relative format (x seconds ago, x minutes ago, about an hour ago, etc.).
        /// or if max days has already passed in FB DateTime format (February 13 at 11:28am or November 5, 2011 at 1:57pm).
        /// </summary>
        /// <param name="dateTime">the DateTime to convert to relative time.</param>
        /// <param name="maxDays">maximum number of days before formatting in FB date-time format (ex. November 5, 2011 at 1:57pm)</param>
        /// <returns></returns>
        public static string ToRelativeDateString(this DateTime? dateTime, int? maxDays = null)
        {
            if(dateTime.HasValue)
            {
                return dateTime.ToRelativeDateString(maxDays);
            }
            else
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Returns the value for <see cref="DateTime.ToShortDateString"/> or empty string if the date is null
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        /// <returns></returns>
        public static string ToShortDateString(this DateTime? dateTime)
        {
            if(dateTime.HasValue)
            {
                return dateTime.Value.ToShortDateString();
            }
            else
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Returns the date of the start of the month for the specified date/time.
        /// For example 3/23/2021 11:15am will return 3/1/2021 00:00:00.
        /// </summary>
        /// <param name="dt">The DateTime.</param>
        /// <returns></returns>
        public static DateTime StartOfMonth(this DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, 1);
        }

        /// <summary>
        /// Returns the Date of the last day of the month.
        /// For example 3/23/2021 11:15am will return 3/31/2021 00:00:00.
        /// </summary>
        /// <param name="dt">The DateTime</param>
        /// <returns></returns>
        public static DateTime EndOfMonth(this DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, 1).AddMonths(1).Subtract(new TimeSpan(1, 0, 0, 0, 0));
        }

        /// <summary>
        /// Returns the date of the start of the week for the specified date/time.
        /// </summary>
        /// <param name="dt">The DateTime.</param>
        /// <param name="startOfWeek">The start of week.</param>
        /// <returns></returns>
        public static DateTime StartOfWeek(this DateTime dt, DayOfWeek startOfWeek)
        {
            int diff = dt.DayOfWeek - startOfWeek;
            if(diff < 0)
            {
                diff += 7;
            }

            return dt.AddDays(-1 * diff).Date;
        }

        /// <summary>
        /// Returns the date of the last day of the week for the specified date/time.
        /// from http://stackoverflow.com/a/38064/1755417
        /// </summary>
        /// <param name="dt">The DateTime.</param>
        /// <param name="startOfWeek">The start of week.</param>
        /// <returns></returns>
        public static DateTime EndOfWeek(this DateTime dt, DayOfWeek startOfWeek)
        {
            return dt.StartOfWeek(startOfWeek).AddDays(6);
        }

        /// <summary>
        ///  Determines whether the DateTime is in the future.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        /// <returns>true if the value is in the future, false if not.</returns>
        public static bool IsFuture(this DateTime dateTime, IDateTimeProvider dateTimeProvider)
        {
            return dateTime > dateTimeProvider.Now;
        }

        /// <summary>
        ///  Determines whether the DateTime is in the past.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        /// <returns>true if the value is in the past, false if not.</returns>
        public static bool IsPast(this DateTime dateTime, IDateTimeProvider dateTimeProvider)
        {
            return dateTime < dateTimeProvider.Now;
        }

        /// <summary>
        ///  Determines whether the DateTime is today.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        /// <returns>true if the value is in the past, false if not.</returns>
        public static bool IsToday(this DateTime dateTime, IDateTimeProvider dateTimeProvider)
        {
            return dateTime.Date == dateTimeProvider.Now.Date;
        }
    }
}

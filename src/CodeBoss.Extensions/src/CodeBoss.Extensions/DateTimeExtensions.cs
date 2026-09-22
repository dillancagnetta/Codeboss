using System;
using System.Collections.Generic;
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
                return start.Value.Age();

            return 0;
        }

        /// <summary>
        /// Returns the age at the current date.
        /// </summary>
        /// <param name="start"></param>
        /// <returns></returns>
        public static int Age(this DateTime start)
        {
            var now = DateTime.Today;
            int age = now.Year - start.Year;
            if(start > now.AddYears(-age))
            {
                age--;
            }

            return age;
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
        /// Get difference in years
        /// </summary>
        /// <param name="startDate"></param>
        /// <param name="endDate"></param>
        /// <returns></returns>
        public static int GetDifferenceInYears(this DateTime startDate, DateTime endDate)
        {
            //source: http://stackoverflow.com/questions/9/how-do-i-calculate-someones-age-in-c
            //this assumes you are looking for the western idea of age and not using East Asian reckoning.
            int age = endDate.Year - startDate.Year;
            if (startDate > endDate.AddYears(-age))
                age--;
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
        public static string ToElapsedString(this DateTime? dateTime, IDateTimeProvider dateTimeProvider, bool condensed = false, bool includeTime = true)
        {
            return dateTime?.ToElapsedString( dateTimeProvider, condensed, includeTime ) ?? string.Empty;
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
        public static string ToRelativeDateString(this DateTime? dateTime, IDateTimeProvider provider, int? maxDays = null)
        {
            return dateTime?.ToRelativeDateString( provider, maxDays ) ?? string.Empty;
        }
        
        /// <summary>
        /// Returns  a string in FB style relative format using UTC as now
        /// </summary>
        /// <param name="dateTime"></param>
        /// <returns></returns>
        public static string ToRelativeDateStringUTC(this DateTime? dateTime)
        {
            return dateTime?.ToRelativeDateString( null ) ?? string.Empty;
        }

        /// <summary>
        /// Returns a string in relative format (x seconds ago, x minutes ago, about an hour ago, in x seconds,
        /// in x minutes, in about an hour, etc.) or if time difference is greater than max days in long format (February
        /// 13 at 11:28am or November 5, 2011 at 1:57pm).
        /// </summary>
        /// <param name="dateTime">the datetime to convert to relative time.</param>
        /// <param name="provider"></param>
        /// <param name="maxDays">maximum number of days before formatting in long format (ex. November 5, 2011 at 1:57pm) </param>
        /// <returns></returns>
        public static string ToRelativeDateString( this DateTime dateTime, IDateTimeProvider provider = null, int? maxDays = null )
        {
            try
            {
                DateTime now = provider?.Now ?? DateTime.UtcNow;

                string nowText = "just now";
                string format = "{0} ago";
                TimeSpan timeSpan = now - dateTime;
                if ( dateTime > now )
                {
                    nowText = "now";
                    format = "in {0}";
                    timeSpan = dateTime - now;
                }

                double seconds = timeSpan.TotalSeconds;
                double minutes = timeSpan.TotalMinutes;
                double hours = timeSpan.TotalHours;
                double days = timeSpan.TotalDays;
                double weeks = days / 7;
                double months = days / 30;
                double years = days / 365;

                // Just return in long format if max days has passed.
                if ( maxDays.HasValue && days > maxDays )
                {
                    if ( now.Year == dateTime.Year )
                    {
                        return dateTime.ToString( @"MMMM d a\t h:mm tt" );
                    }
                    else
                    {
                        return dateTime.ToString( @"MMMM d, yyyy a\t h:mm tt" );
                    }
                }

                if ( Math.Round( seconds ) < 5 )
                {
                    return nowText;
                }
                else if ( minutes < 1.0 )
                {
                    return string.Format( format, Math.Floor( seconds ) + " seconds" );
                }
                else if ( Math.Floor( minutes ) == 1 )
                {
                    return string.Format( format, "1 minute" );
                }
                else if ( hours < 1.0 )
                {
                    return string.Format( format, Math.Floor( minutes ) + " minutes" );
                }
                else if ( Math.Floor( hours ) == 1 )
                {
                    return string.Format( format, "about an hour" );
                }
                else if ( days < 1.0 )
                {
                    return string.Format( format, Math.Floor( hours ) + " hours" );
                }
                else if ( Math.Floor( days ) == 1 )
                {
                    return string.Format( format, "1 day" );
                }
                else if ( weeks < 1 )
                {
                    return string.Format( format, Math.Floor( days ) + " days" );
                }
                else if ( Math.Floor( weeks ) == 1 )
                {
                    return string.Format( format, "1 week" );
                }
                else if ( months < 3 )
                {
                    return string.Format( format, Math.Floor( weeks ) + " weeks" );
                }
                else if ( months <= 12 )
                {
                    return string.Format( format, Math.Floor( months ) + " months" );
                }
                else if ( Math.Floor( years ) <= 1 )
                {
                    return string.Format( format, "1 year" );
                }
                else
                {
                    return string.Format( format, Math.Floor( years ) + " years" );
                }
            }
            catch ( Exception )
            {
            }
            return "";
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

            return string.Empty;
        }

        /// <summary>
        /// Converts a datetime to the short date/time format.
        /// </summary>
        /// <param name="dt">The dt.</param>
        /// <returns></returns>
        public static string ToShortDateTimeString(this DateTime dt)
        {
            return dt.ToShortDateString() + " " + dt.ToShortTimeString();
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

        /// <summary>
        /// Returns countdown to a target date
        /// </summary>
        /// <param name="value"></param>
        /// <param name="endDateTime"></param>
        /// <returns></returns>
        public static string ToDaysTil(this DateTime value, DateTime endDateTime)
        {
            var ts = new TimeSpan(endDateTime.Ticks - value.Ticks);
            var delta = ts.TotalSeconds;
            if(delta < 60)
            {
                return ts.Seconds == 1 ? "one second" : ts.Seconds + " seconds";
            }
            if(delta < 120)
            {
                return "a minute";
            }
            if(delta < 2700) // 45 * 60
            {
                return ts.Minutes + " minutes";
            }
            if(delta < 5400) // 90 * 60
            {
                return "an hour";
            }
            if(delta < 86400) // 24 * 60 * 60
            {
                return ts.Hours + " hours";
            }
            if(delta < 172800) // 48 * 60 * 60
            {
                return "yesterday";
            }
            if(delta < 2592000) // 30 * 24 * 60 * 60
            {
                return ts.Days + " days";
            }
            if(delta < 31104000) // 12 * 30 * 24 * 60 * 60
            {
                int months = Convert.ToInt32(Math.Floor((double)ts.Days / 30));
                return months <= 1 ? "one month" : months + " months";
            }
            var years = Convert.ToInt32(Math.Floor((double)ts.Days / 365));
            return years <= 1 ? "one year" : years + " years";
        }

        /// <summary>
        /// Returns quarter for a datetime
        /// </summary>
        /// <param name="fromDate"></param>
        /// <returns></returns>
        public static int Quarter(this DateTime fromDate)
        {
            return ((fromDate.Month - 1) / 3) + 1;
        }
        
        /// <summary>
        /// Generate Weekly Dates: Start from the calculated target day and step back by 7 days for the specified number of weeks.
        /// </summary>
        /// <param name="startDate"></param>
        /// <param name="targetDay"></param>
        /// <param name="weeks"></param>
        /// <example>foreach (var date in today.GetWeeklyDates(DayOfWeek.Sunday))</example>
        /// <returns></returns>
        public static IEnumerable<DateTime> GetWeeklyDatesFrom(
            this DateTime startDate,
            DayOfWeek targetDay,
            int weeks = 52)
        {
            // Adjust startDate to the first occurrence of the target day of the week on or after the start date
            int daysToTarget = ((int)targetDay - (int)startDate.DayOfWeek + 7) % 7;
            DateTime targetDate = startDate.AddDays(daysToTarget);

            // Generate weekly dates incrementally until today or the max weeks limit
            for (int i = 0; i < weeks; i++)
            {
                if (targetDate > DateTime.Today)
                    yield break;

                yield return targetDate;
                targetDate = targetDate.AddDays(7);
            }
        }

        #region TimeSpan Extensions

        /// <summary>
        /// Returns a TimeSpan as h:mm AM/PM (culture invariant)
        /// Examples: 1:45 PM, 12:01 AM
        /// </summary>
        /// <param name="timespan">The timespan.</param>
        /// <returns></returns>
        public static string ToTimeString(this TimeSpan timespan, IDateTimeProvider dateTimeProvider)
        {
            // since the comments on this say HH:MM AM/PM, make sure to return the time in that format
            return dateTimeProvider.Now.Add(timespan).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns a TimeSpan as h:mm AM/PM (culture invariant)
        /// Examples: 1:45 PM, 12:01 AM
        /// </summary>
        /// <param name="timespan">The timespan.</param>
        /// <returns></returns>
        public static string ToTimeUtcString(this TimeSpan timespan)
        {
            // since the comments on this say HH:MM AM/PM, make sure to return the time in that format
            return DateTime.UtcNow.Add(timespan).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
        }

        #endregion TimeSpan

        #region TimeOnly Extensions
        
        /// <summary>
        /// Returns a TimeSpan as h:mm AM/PM (culture invariant)
        /// Examples: 1:45 PM, 12:01 AM
        /// </summary>
        /// <param name="time">The timespan.</param>
        /// <param name="dateTimeProvider"></param>
        /// <returns></returns>
        public static string ToTimeUtcString(this TimeOnly? time, IDateTimeProvider? dateTimeProvider)
        {
            var now = dateTimeProvider?.Now ?? DateTime.UtcNow;
            // since the comments on this say HH:MM AM/PM, make sure to return the time in that format
            return now.Add(time?.ToTimeSpan() ?? new TimeSpan(0)).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
        }

        #endregion
    }
}

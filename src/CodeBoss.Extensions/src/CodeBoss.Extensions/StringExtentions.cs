using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Codeboss;

namespace CodeBoss.Extensions
{
    public static partial class Extensions
    {
        /// <summary>
        /// Indicates whether this string is null or an System.String.Empty string.
        /// </summary>
        public static bool IsNullOrEmpty(this string str) => string.IsNullOrEmpty(str);

        /// <summary>
        /// indicates whether this string is null, empty, or consists only of white-space characters.
        /// </summary>
        public static bool IsNullOrWhiteSpace(this string str) => string.IsNullOrWhiteSpace(str);

        public static Uri ToUri(this string s)
        {
            if (string.IsNullOrEmpty(s)) return null;

            return new Uri(s, UriKind.Absolute);
        }

        /// <summary>
        /// Adds a char to end of given string if it does not ends with the char.
        /// </summary>
        public static string EnsureEndsWith(this string str, char c, StringComparison comparisonType = StringComparison.Ordinal)
        {
            Check.NotNull(str, nameof(str));

            if (str.EndsWith(c.ToString(), comparisonType))
            {
                return str;
            }

            return str + c;
        }

        /// <summary>
        /// Adds a char to beginning of given string if it does not starts with the char.
        /// </summary>
        public static string EnsureStartsWith(this string str, char c, StringComparison comparisonType = StringComparison.Ordinal)
        {
            Check.NotNull(str, nameof(str));

            if (str.StartsWith(c.ToString(), comparisonType))
            {
                return str;
            }

            return c + str;
        }


        /// <summary>
        /// Converts string enumerable to Guid
        /// </summary>
        public static IEnumerable<Guid> ToGuids(this IEnumerable<string> source)
        {
            if (!source.IsNullOrEmpty())
            {
                return source.Select(x => Guid.Parse(x));
            }

            return new List<Guid>(0);
        }

        /// <summary>
        /// Splits a Camel or Pascal cased identifier into separate words.
        /// </summary>
        /// <param name="str">The identifier.</param>
        /// <returns></returns>
        public static string SplitCase(this string str)
        {
            if(str == null)
            {
                return null;
            }

            return Regex.Replace(Regex.Replace(str, @"(\P{Ll})(\P{Ll}\p{Ll})", "$1 $2"), @"(\p{Ll})(\P{Ll})", "$1 $2");
        }

        /// <summary>
        /// Joins an array of English strings together with commas plus "and" for last element.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <returns>Concatenated string.</returns>
        public static string JoinStringsWithCommaAnd(this IEnumerable<String> source)
        {
            if(source == null || !source.Any())
            {
                return string.Empty;
            }

            var output = string.Empty;

            var list = source.ToList();

            if(list.Count > 1)
            {
                var delimited = string.Join(", ", list.Take(list.Count - 1));

                output = string.Concat(delimited, " and ", list.LastOrDefault());
            }
            else
            {
                // only one element, just use it
                output = list[0];
            }

            return output;
        }
    }
}

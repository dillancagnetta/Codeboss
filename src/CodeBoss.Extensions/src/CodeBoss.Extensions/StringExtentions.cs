using System;
using System.Collections.Generic;
using System.Linq;
using Codeboss;

namespace CodeBoss.Extensions
{
    public static partial class Extentions
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
    }
}

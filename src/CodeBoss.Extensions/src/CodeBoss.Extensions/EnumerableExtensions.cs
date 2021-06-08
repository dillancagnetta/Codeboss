using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CodeBoss.Extensions
{
    public static class EnumerableExtensions
    {
        public static void ForEach<TSource>(this IEnumerable<TSource> source, Action<TSource> action)
        {
            if (source == null || !source.Any()) return;

            foreach (var item in source)
            {
                action(item);
            }
        }

        public static bool IsNullOrEmpty<TSource>(this IEnumerable<TSource> source) => source == null || !source.Any();

        /// <summary>
        /// Adds only distinct items to the source. Able to pass in an optional <see cref="IEqualityComparer{T}"/> to configure
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <param name="source"></param>
        /// <param name="items"></param>
        /// <param name="comparer"></param>
        /// <returns></returns>
        public static IEnumerable<TSource> AddRangeDistinct<TSource>(
            this IEnumerable<TSource> source,
            IEnumerable<TSource> items,
            IEqualityComparer<TSource> comparer = default)
        {
            if (items.IsNullOrEmpty()) return source;
            if (source.IsNullOrEmpty()) source = new List<TSource>(items?.Count() ?? 0);

            foreach (var item in items)
            {
                if (!source.Contains(item, comparer))
                {
                    ((IList<TSource>)source).Add(item);
                }
            }
            return source;
        }


        /// <summary>
        /// Concatenate the items into a Delimited string
        /// </summary>
        /// <example>
        /// FirstNamesList.AsDelimited(",") would be "Ted,Suzy,Noah"
        /// FirstNamesList.AsDelimited(", ", " and ") would be "Ted, Suzy and Noah"
        /// </example>
        /// <typeparam name="T"></typeparam>
        /// <param name="items">The items.</param>
        /// <param name="delimiter">The delimiter.</param>
        /// <param name="finalDelimiter">The final delimiter. Set this if the finalDelimiter should be a different delimiter</param>
        /// <returns></returns>
        public static string AsDelimited<T>(this List<T> items, string delimiter, string finalDelimiter = null)
        {
            return AsDelimited<T>(items, delimiter, finalDelimiter, false);
        }

        /// <summary>
        /// Concatenate the items into a Delimited string an optionally htmlencode the strings
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="items">The items.</param>
        /// <param name="delimiter">The delimiter.</param>
        /// <param name="finalDelimiter">The final delimiter.</param>
        /// <param name="HtmlEncode">if set to <c>true</c> [HTML encode].</param>
        /// <returns></returns>
        public static string AsDelimited<T>(this List<T> items, string delimiter, string finalDelimiter, bool HtmlEncode)
        {

            List<string> strings = new List<string>();
            foreach(T item in items)
            {
                if(item != null)
                {
                    string itemString = item.ToString();
                    if(HtmlEncode)
                    {
                        itemString = HttpUtility.HtmlEncode(itemString);
                    }
                    strings.Add(itemString);
                }
            }

            if(finalDelimiter != null && strings.Count > 1)
            {
                return string.Join(delimiter, strings.Take(strings.Count - 1).ToArray()) + $"{finalDelimiter}{strings.Last()}";
            }
            else
            {
                return string.Join(delimiter, strings.ToArray());
            }
        }
    }
}

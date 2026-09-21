using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBoss.Extensions
{
    public static class LinqExtensions
    {
        /// <summary>
        /// Allows batching collections by a given size
        ///
        /// Usage:
        ///        foreach(var batch in GetData().Batch(100)) { ... }
        ///
        /// </summary>
        /// <returns>Yields batched collections</returns>
        [Obsolete("Use the built-in Enumerable.Chunk instead. This method will be removed in the next major version.")]
        public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int size)
            => source.Chunk(size);
    }
}

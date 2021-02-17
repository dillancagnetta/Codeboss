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
        public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int size)
        {
            T[] bucket = null;
            var count = 0;

            foreach(var item in source)
            {
                if(bucket == null)
                {
                    bucket = new T[size];
                }

                bucket[count++] = item;

                if(count != size)
                {
                    continue;
                }

                // returns the batch
                yield return bucket.Select(x => x);

                bucket = null;
                count = 0;
            }

            if(bucket != null && count > 0)
            {
                yield return bucket.Take(count);
            }
        }
    }
}

using System;

namespace CodeBoss.Extensions
{
    public static partial class Extensions
    {
        /// <summary>
        ///     Enumerates exception collection fetching the original exception, which would be the real
        ///     error. The outer exceptions are wrappers which we are not concerned.
        /// </summary>
        /// <param name="exception"></param>
        /// <returns></returns>
        public static string TraverseException(this Exception exception)
        {
            var innerException = exception;

            string message;
            // Enumerate through exception stack to get to innermost exception
            do
            {
                message = string.IsNullOrEmpty(innerException.Message) ? string.Empty : innerException.Message;
                innerException = innerException.InnerException;
            } while(innerException != null);

            return message;
        }
    }
}

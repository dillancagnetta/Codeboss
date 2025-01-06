using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CodeBoss.Extensions
{
    public static partial class Extensions
    {
        public static string Name(this Type type) => type.Name;

        public static string ToSnakeCase(this Type type, int index = 0)
        {
            Regex _pattern = new Regex("(?<=[a-z0-9])[A-Z]", RegexOptions.Compiled);
            string _separator = "_";

            var name = _pattern.Replace(type.Name, m => _separator + m.Value).ToLowerInvariant();
            name = name.Substring(index, name.Length-1);
            return name;
        }

        public static string ToQueueName(this Type type) => ToSnakeCase(type, 1);

        public static bool HasAttribute(this Type type, Type attributeType)
        {
            var attributes = type.GetTypeInfo().GetCustomAttributes(attributeType, false);
            return attributes.Any();
        }
        
        
        /// <summary>
        /// Gets the generic type arguments associated with the given
        /// <paramref name="genericType"/> which must be an ancestor of <paramref name="type"/>.
        /// </summary>
        /// <param name="type">The type to inspect.</param>
        /// <param name="genericType">The generic type ancestor whose arguments are to be retrieved.</param>
        /// <returns>An array of <see cref="Type" /> objects.</returns>
        /// <exception cref="ArgumentException">Must be a generic type definition. - genericType</exception>
        /// <exception cref="ArgumentException">Type was not a descendend. - genericType</exception>
        public static Type[] GetGenericArgumentsOfBaseType( this Type type, Type genericType )
        {
            if ( !genericType.IsGenericTypeDefinition )
            {
                throw new ArgumentException( "Must be a generic type definition.", nameof( genericType ) );
            }

            while ( type != null && type != typeof( object ) )
            {
                if ( type.IsGenericType && type.GetGenericTypeDefinition() == genericType )
                {
                    return type.GetGenericArguments();
                }

                type = type.BaseType;
            }

            throw new ArgumentException( "Type was not a descendend.", nameof( genericType ) );
        }

    }
}

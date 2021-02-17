using System;
using System.Text.RegularExpressions;

namespace CodeBoss.Extensions
{
    public static partial class Extentions
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

    }
}

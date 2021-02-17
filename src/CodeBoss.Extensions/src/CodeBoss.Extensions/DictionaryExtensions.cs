using System.Collections.Generic;
using System.Dynamic;

namespace CodeBoss.Extensions
{
    public static partial class Extensions
    {
        public static dynamic DictionaryToObject(this IDictionary<string, object> dict)
        {
            IDictionary<string, object> eo = new ExpandoObject();
            foreach (KeyValuePair<string, object> kvp in dict)
            {
                eo.Add(kvp);
            }
            return eo;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;

namespace CodeBoss.Extensions
{
    public static class ExpandObjectExtensions
    {

        /*
         * Usage:
         * ----------------
         *  public class A 
            {
                public int Val1 { get; set; }
            }

            // Somewhere in your app...
            dynamic expando = new ExpandoObject();
            expando.Val1 = 11;

            // Now you got a new instance of A where its Val1 has been set to 11!
            A instanceOfA = ((ExpandoObject)expando).ToObject<A>();
         *
         */
        public static TObject ToObject<TObject>(
            this IDictionary<string, object> someSource,
            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public)
                where TObject : class, new()
        {
            TObject targetObject = new TObject();
            Type targetObjectType = typeof(TObject);

            // Go through all bound target object type properties...
            foreach (PropertyInfo property in
                targetObjectType.GetProperties(bindingFlags))
            {
                // ...and check that both the target type property name and its type matches
                // its counterpart in the ExpandoObject
                if (someSource.ContainsKey(property.Name)
                    && property.PropertyType == someSource[property.Name].GetType())
                {
                    property.SetValue(targetObject, someSource[property.Name]);
                }
            }

            return targetObject;
        }
    }
}

using System;

namespace Codeboss.Types
{
    public interface IEnumeration<TType>
    {
        TType Value { get; set; }
    }

    public class Enumeration<TEnumeration, TType> : IEnumeration<TType> 
        where TEnumeration : class
        where TType: class
    {
        public TType Value { get; set; }

        // Cast Conversion Operator Overloading
        public static implicit operator string(Enumeration<TEnumeration, TType> x) => x.Value.ToString();

        // Assignment Operator  Overloading
        public static implicit operator Enumeration<TEnumeration, TType>(string value) => new(){Value = value as TType};

        public override string ToString() => Value.ToString();
    }
}

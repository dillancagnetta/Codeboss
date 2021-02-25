namespace Codeboss.Types
{
    public interface IEnumeration<TType>
    {
        TType Value { get; set; }
    }

    public class Enumeration<TEnumeration, TType> : IEnumeration<TType>
    {
        public TType Value { get; set; }

        // Cast Conversion Operator Overloading
        public static implicit operator string(Enumeration<TEnumeration, TType> x) => x.Value.ToString();

        public override string ToString() => Value.ToString();
    }
}

namespace Codeboss.Types
{
    public interface IEntity<out T>
    {
        T Id { get; }
    }
}

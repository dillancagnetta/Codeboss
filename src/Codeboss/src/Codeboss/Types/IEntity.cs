namespace Codeboss.Types
{
    public interface IEntity<out TPrimaryKey>
    {
        TPrimaryKey Id { get; }
    }
}

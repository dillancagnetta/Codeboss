namespace Codeboss.Types
{
    public interface IAggregateRoot<out TPrimaryKey> : IEntity<TPrimaryKey>
    {
    }
}

namespace CodeBoss.CQRS.Queries
{
    //Marker
    public interface IQuery
    {
    }

    public interface IQuery<TResult> : IQuery
    {
    }
}

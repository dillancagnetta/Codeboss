namespace Codeboss.Types
{
    public interface ICurrentUser
    {
        bool IsAuthenticated { get; }
        string Id { get; }
    }
}

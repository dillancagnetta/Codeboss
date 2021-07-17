using Codeboss.Types;

namespace CodeBoss.MultiTenant
{
    /// <summary>
    /// Extension of CurrentUser to provide tenant info
    /// </summary>
    public interface ITenantCurrentUser : ICurrentUser
    {
        public string Tenant { get; }
    }
}

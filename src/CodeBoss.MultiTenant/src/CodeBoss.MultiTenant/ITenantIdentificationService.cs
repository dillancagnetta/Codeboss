namespace CodeBoss.MultiTenant
{
    /// <summary>
    /// Determines which tenant is currently active
    /// </summary>
    public interface ITenantIdentificationService<out TTenant>
    {
        TTenant CurrentTenant();
    }
}

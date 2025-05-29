namespace CodeBoss.MultiTenant
{
    /// <summary>
    /// Determines which tenant is currently active
    /// </summary>
    public interface ITenantIdentificationService
    {
        ITenant CurrentTenant();
    }
}

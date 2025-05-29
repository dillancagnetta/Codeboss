namespace CodeBoss.MultiTenant.Providers;

public class DefaultTenantProvider : ITenantProvider
{
    public ITenant CurrentTenant { get; set; }
}
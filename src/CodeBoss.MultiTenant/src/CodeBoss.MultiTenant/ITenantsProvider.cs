namespace CodeBoss.MultiTenant;

public interface ITenantsProvider<out TTenant> : ITenantProvider
{
    public bool Enabled { get; }
    TTenant[] Tenants();
    TTenant Get(string name);
}

public interface ITenantProvider
{
    ITenant CurrentTenant { get; set; }
}
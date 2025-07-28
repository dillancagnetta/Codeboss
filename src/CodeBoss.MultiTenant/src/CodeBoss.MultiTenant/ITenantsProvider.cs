namespace CodeBoss.MultiTenant;

public interface ITenantsProvider<out TTenant> : ISimpleTenantsProvider where TTenant : ITenant
{
    public bool Enabled { get; }
    TTenant[] Tenants();
    TTenant Get(string name);
}

public interface ITenantProvider
{
    ITenant CurrentTenant { get; set; }
}

public interface ISimpleTenantsProvider
{
    ITenant[] Tenants();
}
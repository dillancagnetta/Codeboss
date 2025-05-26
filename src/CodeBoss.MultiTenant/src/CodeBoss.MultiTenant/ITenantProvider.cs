namespace CodeBoss.MultiTenant
{
    public interface ITenantProvider<out TTenant>
    {
        public bool Enabled { get; }
        TTenant[] Tenants();
        TTenant Get(string name);
    }
}

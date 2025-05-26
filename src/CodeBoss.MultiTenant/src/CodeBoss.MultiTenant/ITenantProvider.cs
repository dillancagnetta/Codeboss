namespace CodeBoss.MultiTenant
{
    public interface ITenantProvider<TTenant>
    {
        public bool Enabled { get; }
        TTenant[] Tenants();
        TTenant Get(string name);
        TTenant CurrentTenant { get; set; }
    }
}

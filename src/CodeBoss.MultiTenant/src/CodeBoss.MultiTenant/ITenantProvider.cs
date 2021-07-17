namespace CodeBoss.MultiTenant
{
    public interface ITenantProvider
    {
        public bool Enabled { get; }
        ITenant[] Tenants();
        ITenant Get(string name);
        ITenant CurrentTenant { get; set; }
    }
}

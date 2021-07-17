namespace CodeBoss.MultiTenant
{
    public class MultiTenantOptions
    {
        public bool Enabled { get; set; }
        public ITenant[] Tenants { get; set; }
    }
}

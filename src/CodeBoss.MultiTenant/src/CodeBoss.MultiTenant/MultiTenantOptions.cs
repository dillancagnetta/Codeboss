namespace CodeBoss.MultiTenant
{
    public class MultiTenantOptions
    {
        public bool Enabled { get; set; }
        public Tenant[] Tenants { get; set; }
    }
}

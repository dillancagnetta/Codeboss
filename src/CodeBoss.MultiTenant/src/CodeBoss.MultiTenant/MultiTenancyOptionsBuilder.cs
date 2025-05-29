using System;

namespace CodeBoss.MultiTenant
{
    public class MultiTenancyOptionsBuilder
    {
        public Type TenantProvider { get; set; }
        public Type TenantsProvider { get; set; }
    }
}

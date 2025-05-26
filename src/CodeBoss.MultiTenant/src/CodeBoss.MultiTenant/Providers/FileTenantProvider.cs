﻿using System.Linq;
using Microsoft.Extensions.Options;

namespace CodeBoss.MultiTenant.Providers
{
    public class FileTenantProvider : ITenantProvider<ITenant>
    {
        private readonly MultiTenantOptions _options;

        public FileTenantProvider(IOptions<MultiTenantOptions> options) => _options = options.Value;

        public bool Enabled => _options.Enabled;
        public ITenant[] Tenants() => _options.Tenants;
        public ITenant Get(string name) => Tenants().FirstOrDefault(t => t.Name == name);
    }
}

using System.Linq;
using Microsoft.AspNetCore.Http;

namespace CodeBoss.MultiTenant.Identification
{
    /// <summary>
    /// Reads query string to determine the tenant
    /// </summary>
    public class QueryStringTenantIdentificationService : ITenantIdentificationService
    {
        private readonly IHttpContextAccessor _accessor;
        private readonly ITenantProvider _provider;

        public QueryStringTenantIdentificationService(IHttpContextAccessor accessor, ITenantProvider provider)
        {
            _accessor = accessor;
            _provider = provider;
        }

        public ITenant CurrentTenant()
        {
            if(_accessor.HttpContext != null)
            {
                var tenantName = _accessor.HttpContext.Request.Query["Tenant"].ToString();

                if(!string.IsNullOrWhiteSpace(tenantName))
                {
                    return _provider.Get(tenantName);
                }
            }

            return _provider.Tenants().FirstOrDefault();
        }
    }
}

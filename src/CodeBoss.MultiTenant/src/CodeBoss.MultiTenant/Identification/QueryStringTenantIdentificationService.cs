using System.Linq;
using Microsoft.AspNetCore.Http;

namespace CodeBoss.MultiTenant.Identification
{
    /// <summary>
    /// Reads query string to determine the tenant
    /// Not used, just an example for demonstration.
    /// </summary>
    public class QueryStringTenantIdentificationService<T> : ITenantIdentificationService<T>
    {
        private readonly IHttpContextAccessor _accessor;
        private readonly ITenantsProvider<T> _provider;

        public QueryStringTenantIdentificationService(IHttpContextAccessor accessor, ITenantsProvider<T> provider)
        {
            _accessor = accessor;
            _provider = provider;
        }

        public T CurrentTenant()
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

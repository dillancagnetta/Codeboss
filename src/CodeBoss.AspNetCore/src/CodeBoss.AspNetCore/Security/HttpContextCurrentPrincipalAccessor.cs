using System.Security.Claims;
using System.Threading;
using Codeboss.Types;
using Microsoft.AspNetCore.Http;

namespace CodeBoss.AspNetCore.Security
{
    public class HttpContextCurrentPrincipalAccessor : ICurrentPrincipalAccessor
    {
        public ClaimsPrincipal Principal => GetClaimsPrincipal();

        private readonly IHttpContextAccessor _httpContextAccessor;
        public HttpContextCurrentPrincipalAccessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        protected ClaimsPrincipal GetClaimsPrincipal()
        {
            return _httpContextAccessor.HttpContext?.User ?? Thread.CurrentPrincipal as ClaimsPrincipal;
        }
    }
}

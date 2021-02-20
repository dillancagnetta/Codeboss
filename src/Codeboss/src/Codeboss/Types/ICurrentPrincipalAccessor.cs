using System.Security.Claims;

namespace Codeboss.Types
{
    public interface ICurrentPrincipalAccessor
    {
        ClaimsPrincipal Principal { get; }
    }
}

using CodeBoss.AspNetCore.Security;
using Codeboss.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodeBoss.AspNetCore
{
    public static class ConfigureServices
    {
        public static void AddAspNetCurrentUser<TCurrentUser>(this IServiceCollection services) where TCurrentUser : class, ICurrentUser
        {
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddSingleton<ICurrentPrincipalAccessor, HttpContextCurrentPrincipalAccessor>();
            services.AddScoped<ICurrentUser, TCurrentUser>();
        }
    }
}

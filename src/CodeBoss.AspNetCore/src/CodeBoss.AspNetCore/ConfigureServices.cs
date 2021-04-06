using CodeBoss.AspNetCore.CbDateTime;
using CodeBoss.AspNetCore.Security;
using Codeboss.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;

namespace CodeBoss.AspNetCore
{
    public static class ConfigureServices
    {
        /// <summary>
        /// Registers a AspNet Current User with required Http Context accessors
        /// </summary>
        /// <typeparam name="TCurrentUser">Interface</typeparam>
        /// <typeparam name="TCurrentUserImp">Implementation</typeparam>
        /// <param name="services"></param>
        public static void AddAspNetCurrentUser<TCurrentUser, TCurrentUserImp>(this IServiceCollection services)
            where TCurrentUser : class, ICurrentUser
            where TCurrentUserImp : class, TCurrentUser
        {
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddSingleton<ICurrentPrincipalAccessor, HttpContextCurrentPrincipalAccessor>();
            services.AddScoped<TCurrentUser, TCurrentUserImp>();
        }


        public static IServiceCollection AddCodeBossDateTime(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<DateTimeOptions>(configuration.GetSection(nameof(DateTimeOptions)));
            services.AddTransient<IDateTimeProvider, CodeBossDateTimeProvider>();
            return services;
        }
    }
}

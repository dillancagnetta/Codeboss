using System;
using CodeBoss.MultiTenant.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodeBoss.MultiTenant
{
    public static class ConfigureServices
    {
        public static IServiceCollection AddCodeBossMultiTenancy(this IServiceCollection services, IConfiguration configuration, Action<MultiTenancyOptionsBuilder> optionsAction = null)
        {
            services.Configure<MultiTenantOptions>(configuration.GetSection(nameof(MultiTenantOptions)));

            // Allows specifying custom ITenantProvider implementation
            if(optionsAction is not null)
            {
                var builder = new MultiTenancyOptionsBuilder();
                optionsAction(builder);

                services.AddSingleton(typeof(ITenantsProvider<>), builder.TenantProvider);
                services.AddSingleton(typeof(ITenantProvider), builder.TenantProvider);

                return services;
            }

            // Default ITenantProvider  is appsettings file
            // Only registers the service if no service of the same type is already registered
            // This allows for easy override if needed
            services.TryAddSingleton<ITenantsProvider<ITenant>, FileTenantsProvider>();
            services.TryAddSingleton<ITenantProvider, FileTenantsProvider>();
            
            return services;
        }
    }
}

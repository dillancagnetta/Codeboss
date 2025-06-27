using System;
using CodeBoss.MultiTenant.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodeBoss.MultiTenant
{
    public static class ConfigureServices
    {
        public static IServiceCollection AddCodeBossMultiTenancy<TTenant>(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<MultiTenancyOptionsBuilder> optionsAction = null) where TTenant : ITenant 
        {
            services.Configure<MultiTenantOptions>(configuration.GetSection(nameof(MultiTenantOptions)));

            // Allows specifying custom ITenantProvider implementation
            if(optionsAction is not null)
            {
                var builder = new MultiTenancyOptionsBuilder();
                optionsAction(builder);
                
                if (!typeof(ITenantsProvider<TTenant>).IsAssignableFrom(builder.TenantsProvider))
                {
                    throw new ArgumentException(
                        $"{builder.TenantsProvider.Name} must implement ITenantsProvider<{typeof(TTenant).Name}>");
                }
                
                services.AddScoped(typeof(ITenantsProvider<TTenant>), builder.TenantsProvider);
                // This singleton to provide the tenant to the application
                // services.AddSingleton<ITenantProvider, DefaultTenantProvider>();

                return services;
            }

            // Default ITenantProvider  is appsettings file
            // Only registers the service if no service of the same type is already registered
            // This allows for easy override if needed
            services.TryAddScoped<ITenantsProvider<ITenant>, FileTenantsProvider>();
            // This singleton to provide the tenant to the application
            services.AddSingleton<ITenantProvider, DefaultTenantProvider>();
            
            return services;
        }
    }
}

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodeBoss.AspNetCore.Startup
{
    public static class Extensions
    {
        public static IServiceCollection TryAddStartupInitializer(this IServiceCollection services)
        {
            services.TryAddSingleton<IStartupInitializer, StartupInitializer>();
            services.TryAddStartupHostedService();

            return services;
        }

        /// <summary>
        /// Try Add the Startup Hosted service
        /// </summary>
        /// <param name="services"></param>
        private static IServiceCollection TryAddStartupHostedService(this IServiceCollection services)
        {
            var provider = services.BuildServiceProvider();
            var hostedService = provider?.GetService<StartupHostedService>();
            if (hostedService == null)
            {
                services.AddHostedService<StartupHostedService>();
            }

            return services;
        }

        public static IServiceCollection AddInitializer<TInitializer>(this IServiceCollection services)
            where TInitializer : class, IInitializer
        {
            // Adds this to the list of IInitializers
            services.AddTransient<IInitializer, TInitializer>();
            // This should only be called once, so we use Try
            services.TryAddStartupInitializer();

            return services;
        }

        #region Runners

        public static Task RunInitializers(this IServiceCollection services)
        {
            var provider = services.BuildServiceProvider();
            var startupInitializer = provider?.GetService<IStartupInitializer>();

            return startupInitializer?.InitializeAsync() ?? Task.CompletedTask;
        }

        /// <summary>
        /// Call this method once the containers are build and we have the <see cref="IServiceProvider"/>
        /// </summary>
        /// <param name="provider"></param>
        public static Task RunInitializers(this IServiceProvider provider)
        {
            var startupInitializer = provider.GetService<IStartupInitializer>();

            return startupInitializer?.InitializeAsync() ?? Task.CompletedTask;
        }

        #endregion
    }
}

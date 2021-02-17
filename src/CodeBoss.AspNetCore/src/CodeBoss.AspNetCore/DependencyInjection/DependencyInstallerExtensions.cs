using System;
using System.Linq;
using System.Reflection;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CodeBoss.AspNetCore.DependencyInjection
{
    public static class DependencyInstallerExtensions
    {
        public static void InstallServicesInAssemblies(
            this IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment,
            params Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                var installers = assembly.ExportedTypes
                        .Where(x =>
                            typeof(IDependencyInstaller).IsAssignableFrom(x) &&
                            !x.IsInterface &&
                            !x.IsAbstract)
                        .Select(Activator.CreateInstance)
                        .Cast<IDependencyInstaller>()
                        .ToList();

                installers.ForEach(installer => installer.InstallServices(services, configuration, environment));
            }
        }

        public static void InstallServicesInAssemblies(
            this ContainerBuilder builder,
            IConfiguration configuration,
            IHostEnvironment environment,
            params Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                var installers = assembly.ExportedTypes
                    .Where(x =>
                        typeof(IAutofacDependencyInstaller).IsAssignableFrom(x) &&
                        !x.IsInterface &&
                        !x.IsAbstract)
                    .Select(Activator.CreateInstance)
                    .Cast<IAutofacDependencyInstaller>()
                    .ToList();

                installers.ForEach(installer => installer.InstallServices(builder, configuration, environment));
            }
        }
    }
}

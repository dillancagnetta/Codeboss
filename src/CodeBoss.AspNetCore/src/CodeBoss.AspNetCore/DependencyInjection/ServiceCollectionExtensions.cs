using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace CodeBoss.AspNetCore.DependencyInjection
{
    // https://stackoverflow.com/questions/39174989/how-to-register-multiple-implementations-of-the-same-interface-in-asp-net-core
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection RegisterAllTypes<T>(
            this IServiceCollection services,
            Assembly[] assemblies,
            ServiceLifetime lifetime = ServiceLifetime.Transient)
        {
            var typesFromAssemblies = assemblies.SelectMany(a => a.DefinedTypes.Where(x => x.GetInterfaces().Contains(typeof(T))));
            foreach (var type in typesFromAssemblies)
            {
                services.Add(new ServiceDescriptor(typeof(T), type, lifetime));
            }

            return services;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodeBoss.AspNetCore.DependencyInjection
{
    // https://stackoverflow.com/questions/39174989/how-to-register-multiple-implementations-of-the-same-interface-in-asp-net-core
    /// <summary>
    /// Registers all implementations of the same interface
    /// </summary>
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

        /// <summary>
        /// Registers all implementations of the same interface
        /// </summary>
        public static IServiceCollection RegisterAllTypes(
            this IServiceCollection services,
            Type registrationType,
            Assembly[] assemblies,
            Func<TypeInfo, bool> predicate = null,
            ServiceLifetime lifetime = ServiceLifetime.Transient)
        {
            IEnumerable<TypeInfo> typesFromAssemblies = new TypeInfo[]{};
            if (predicate != null)
            {
                typesFromAssemblies = assemblies
                    .SelectMany(a => a.DefinedTypes
                        .Where(x => x.GetInterfaces().Contains(registrationType) && predicate(x)));
            }
            else
            {
                typesFromAssemblies = assemblies
                    .SelectMany(a => a.DefinedTypes
                        .Where(x => x.GetInterfaces().Contains(registrationType)));
            }
            
            foreach(var type in typesFromAssemblies)
            {
                services.Add(new ServiceDescriptor(registrationType, type, lifetime));
            }

            return services;
        }

        public static bool IsAdded<T>(this IServiceCollection services)
        {
            return services.IsAdded(typeof(T));
        }

        public static bool IsAdded(this IServiceCollection services, Type type)
        {
            return services.Any(d => d.ServiceType == type);
        }

        /// <summary>
        /// Usage:
        ///      var serviceProvider =   new ServiceCollection()
        ///             .ReplaceService<IMyService>(sp => new MyService(), ServiceLifetime.Singleton)
        ///             .BuildServiceProvider();
        /// </summary>
        public static IServiceCollection ReplaceService<TService>(
            this IServiceCollection services,
            Func<IServiceProvider, TService> implementationFactory,
            ServiceLifetime lifetime)
            where TService : class
        {
            var descriptorToRemove = services.FirstOrDefault(d => d.ServiceType == typeof(TService));

            services.Remove(descriptorToRemove);

            var descriptorToAdd = new ServiceDescriptor(typeof(TService), implementationFactory, lifetime);

            services.Add(descriptorToAdd);

            return services;
        }


        /// <summary>
        /// Usage: 
        ///             services.ReplaceService<IConnectionStringResolver, MultiTenantConnectionStringResolver>(ServiceLifetime.Transient);
        /// </summary>
        public static IServiceCollection ReplaceService<TService, TImplementation>(
            this IServiceCollection services,
            ServiceLifetime lifetime)
            where TService : class
        {
            var descriptor = new ServiceDescriptor(typeof(TService), typeof(TImplementation), lifetime);

            services.Replace(descriptor);

            return services;
        }
    }
}

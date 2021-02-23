using System;
using CodeBoss.AspNetCore.DependencyInjection;
using CodeBoss.Extensions;
using Codeboss.Types;
using Microsoft.Extensions.DependencyInjection;

namespace CodeBoss.CQRS.Queries
{
    public static class ConfigureServices
    {
        public static IServiceCollection AddQueryHandlers(this IServiceCollection services)
        {
            services.RegisterAllTypes(
                typeof(IQueryHandler<,>),
                AppDomain.CurrentDomain.GetAssemblies(),
                type => !type.HasAttribute(typeof(DecoratorAttribute)),
                ServiceLifetime.Transient);

            return services;
        }

        public static IServiceCollection AddInMemoryQueryDispatcher(this IServiceCollection services)
        {
            services.AddSingleton<IQueryDispatcher, QueryDispatcher>();
            return services;
        }
    }
}

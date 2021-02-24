using System;
using System.Reflection;
using Codeboss.Types;
using Microsoft.Extensions.DependencyInjection;

namespace CodeBoss.CQRS.Queries
{
    public static class ConfigureServices
    {
        public static IServiceCollection AddQueryHandlers(this IServiceCollection services, Assembly assembly)
        {
            services.Scan(s =>
                s.FromAssemblies(AppDomain.CurrentDomain.GetAssemblies())
                    .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>))
                        .WithoutAttribute(typeof(DecoratorAttribute)))
                    .AsImplementedInterfaces()
                    .WithTransientLifetime());

            return services;
        }

        public static IServiceCollection AddInMemoryQueryDispatcher(this IServiceCollection services)
        {
            services.AddSingleton<IQueryDispatcher, QueryDispatcher>();
            return services;
        }
    }
}

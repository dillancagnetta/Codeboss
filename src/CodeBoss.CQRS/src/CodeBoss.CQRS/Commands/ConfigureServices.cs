using System;
using Codeboss.Types;
using CodeBoss.AspNetCore.DependencyInjection;
using CodeBoss.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace CodeBoss.CQRS.Commands
{
    public static class ConfigureServices
    {
        public static IServiceCollection AddCommandHandlers(this IServiceCollection services)
        {
            services.RegisterAllTypes(
                typeof(ICommandHandler<>),
                AppDomain.CurrentDomain.GetAssemblies(),
                type => !type.HasAttribute(typeof(DecoratorAttribute)),
                ServiceLifetime.Transient);

            return services;
        }

        public static IServiceCollection AddInMemoryCommandDispatcher(this IServiceCollection services)
        {
            services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
            return services;
        }
    }
}

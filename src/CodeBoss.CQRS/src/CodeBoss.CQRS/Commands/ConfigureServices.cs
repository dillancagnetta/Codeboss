using System;
using Codeboss.Types;
using Microsoft.Extensions.DependencyInjection;

namespace CodeBoss.CQRS.Commands
{
    public static class ConfigureServices
    {
        public static IServiceCollection AddCommandHandlers(this IServiceCollection services)
        {
            services.Scan(s =>
                s.FromAssemblies(AppDomain.CurrentDomain.GetAssemblies())
                    .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<>))
                        .WithoutAttribute(typeof(DecoratorAttribute)))
                    .AsImplementedInterfaces()
                    .WithTransientLifetime());

            return services;
        }

        public static IServiceCollection AddInMemoryCommandDispatcher(this IServiceCollection services)
        {
            services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
            return services;
        }
    }
}

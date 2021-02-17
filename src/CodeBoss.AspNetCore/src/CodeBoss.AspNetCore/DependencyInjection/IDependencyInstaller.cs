using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CodeBoss.AspNetCore.DependencyInjection
{
    public interface IDependencyInstaller
    {
        void InstallServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment);
    }

    public interface IAutofacDependencyInstaller
    {
        void InstallServices(ContainerBuilder builder, IConfiguration configuration, IHostEnvironment environment);
    }
}

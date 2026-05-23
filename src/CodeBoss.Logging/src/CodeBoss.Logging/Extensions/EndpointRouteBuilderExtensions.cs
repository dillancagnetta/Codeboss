using CodeBoss.Logging.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CodeBoss.Logging;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapLogLevelHandler(
        this IEndpointRouteBuilder builder, string endpointRoute = "~/logging/level")
    {
        return builder.MapPost(endpointRoute, async context =>
        {
            var service = context.RequestServices.GetService<ILoggingService>();
            if (service is null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("ILoggingService is not registered. Add UseCodeBossLogging() to your Program.cs.");
                return;
            }

            var level = context.Request.Query["level"].ToString();
            if (string.IsNullOrEmpty(level))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Invalid value for logging level.");
                return;
            }

            service.SetLoggingLevel(level);
            context.Response.StatusCode = 200;
        });
    }
}

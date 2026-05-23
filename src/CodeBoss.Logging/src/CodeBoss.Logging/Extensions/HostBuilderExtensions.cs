using Codeboss.Types;
using CodeBoss.Logging.Options;
using CodeBoss.Logging.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Filters;

namespace CodeBoss.Logging;

public static class HostBuilderExtensions
{
    public static IHostBuilder UseCodeBossLogging(
        this IHostBuilder hostBuilder,
        Action<HostBuilderContext, LoggerConfiguration>? configure = null,
        string loggerSectionName = "logger",
        string appSectionName = "app")
    {
        var levelSwitch = new LoggingLevelSwitch();

        return hostBuilder
            .ConfigureServices(services =>
                services.AddSingleton<ILoggingService>(new LoggingService(levelSwitch)))
            .UseSerilog((context, loggerConfig) =>
            {
                var loggerOptions = BindOptions<LoggerOptions>(context.Configuration, loggerSectionName);
                var appOptions = BindOptions<AppOptions>(context.Configuration, appSectionName);

                levelSwitch.MinimumLevel = LoggingService.GetLogEventLevel(loggerOptions.Level);

                loggerConfig
                    .Enrich.FromLogContext()
                    .MinimumLevel.ControlledBy(levelSwitch)
                    .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
                    .Enrich.WithProperty("Application", appOptions.Name ?? appOptions.Service ?? "")
                    .Enrich.WithProperty("Instance", appOptions.Instance ?? "")
                    .Enrich.WithProperty("Version", appOptions.Version ?? "");

                if (loggerOptions.Tags is not null)
                    foreach (var (key, value) in loggerOptions.Tags)
                        loggerConfig.Enrich.WithProperty(key, value);

                if (loggerOptions.MinimumLevelOverrides is not null)
                    foreach (var (source, level) in loggerOptions.MinimumLevelOverrides)
                        loggerConfig.MinimumLevel.Override(source, LoggingService.GetLogEventLevel(level));

                if (loggerOptions.ExcludePaths is not null)
                    foreach (var path in loggerOptions.ExcludePaths)
                        loggerConfig.Filter.ByExcluding(
                            Matching.WithProperty<string>("RequestPath", n => n.EndsWith(path)));

                if (loggerOptions.ExcludeProperties is not null)
                    foreach (var prop in loggerOptions.ExcludeProperties)
                        loggerConfig.Filter.ByExcluding(Matching.WithProperty(prop));

                // Console sink
                if (loggerOptions.Console?.Enabled != false)
                    loggerConfig.WriteTo.Console();

                // File sink
                var fileOpts = loggerOptions.File;
                if (fileOpts?.Enabled == true)
                {
                    var path = string.IsNullOrWhiteSpace(fileOpts.Path) ? "logs/logs.txt" : fileOpts.Path;
                    var interval = Enum.TryParse<RollingInterval>(fileOpts.Interval, true, out var ri)
                        ? ri : RollingInterval.Day;
                    loggerConfig.WriteTo.File(path, rollingInterval: interval,
                        fileSizeLimitBytes: 1_073_741_824, retainedFileCountLimit: 31);
                }

                configure?.Invoke(context, loggerConfig);
            });
    }

    private static T BindOptions<T>(IConfiguration configuration, string sectionName) where T : class, new()
    {
        var options = new T();
        configuration.GetSection(sectionName).Bind(options);
        return options;
    }
}

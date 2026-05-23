using Serilog.Core;
using Serilog.Events;

namespace CodeBoss.Logging.Services;

public class LoggingService : ILoggingService
{
    private readonly LoggingLevelSwitch _levelSwitch;

    public LoggingService(LoggingLevelSwitch levelSwitch)
    {
        _levelSwitch = levelSwitch;
    }

    public void SetLoggingLevel(string level)
    {
        _levelSwitch.MinimumLevel = GetLogEventLevel(level);
    }

    internal static LogEventLevel GetLogEventLevel(string level)
    {
        return Enum.TryParse<LogEventLevel>(level, true, out var result)
            ? result
            : LogEventLevel.Information;
    }
}

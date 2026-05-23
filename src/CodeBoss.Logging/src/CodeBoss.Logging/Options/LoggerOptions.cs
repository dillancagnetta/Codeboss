namespace CodeBoss.Logging.Options;

public class LoggerOptions
{
    public string Level { get; set; } = "information";
    public IDictionary<string, string>? MinimumLevelOverrides { get; set; }
    public IDictionary<string, object>? Tags { get; set; }
    public IEnumerable<string>? ExcludePaths { get; set; }
    public IEnumerable<string>? ExcludeProperties { get; set; }
    public ConsoleOptions? Console { get; set; }
    public FileOptions? File { get; set; }
}

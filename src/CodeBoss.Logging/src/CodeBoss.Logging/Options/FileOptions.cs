namespace CodeBoss.Logging.Options;

public class FileOptions
{
    public bool Enabled { get; set; }
    public string Path { get; set; } = "logs/logs.txt";
    public string Interval { get; set; } = "day";
}

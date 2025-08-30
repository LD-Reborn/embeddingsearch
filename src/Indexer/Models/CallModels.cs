using Microsoft.Extensions.Diagnostics.HealthChecks;

public class CallConfig
{
    public required string Type { get; set; }
    public long? Interval { get; set; } // For Type: Interval
    public string? Path { get; set; } // For Type: FileSystemWatcher
    public string? Schedule { get; set; } // For Type: Schedule
}
public interface ICall
{
    public HealthCheckResult HealthCheck();
    public void Start();
    public void Stop();
    public bool IsEnabled { get; set; }
    public bool IsExecuting { get; set; }
    public CallConfig CallConfig { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }
}
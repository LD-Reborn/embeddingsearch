using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Indexer.Models;

public class CallConfig
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public long? Interval { get; set; } // For Type: Interval
    public string? Path { get; set; } // For Type: FileSystemWatcher
    public string? Schedule { get; set; } // For Type: Schedule
    public List<string>? Events { get; set; } // For Type: Schedule
    public List<string>? Filters { get; set; } // For Type: Schedule
    public bool? IncludeSubdirectories { get; set; } // For Type: Schedule
}
public interface ICall
{
    public HealthCheckResult HealthCheck();
    public void Start();
    public void Stop();
    public void Dispose();
    public string Name { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsExecuting { get; set; }
    public CallConfig CallConfig { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }
}
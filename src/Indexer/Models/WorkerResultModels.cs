using System.Text.Json.Serialization;
using System.Text.Json;

namespace Indexer.Models;

public class WorkerListResults
{
    [JsonPropertyName("WorkerList")]
    public required List<WorkerListResult> Workers { get; set; }
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

public class WorkerListResult
{
    [JsonPropertyName("Name")]
    public required string Name { get; set; }
    [JsonPropertyName("Script")]
    public required string Script { get; set; }
    [JsonPropertyName("IsExecuting")]
    public required bool IsExecuting { get; set; }
    [JsonPropertyName("LastExecution")]
    public required DateTime? LastExecution { get; set; }
    [JsonPropertyName("LastSuccessfulExecution")]
    public required DateTime? LastSuccessfulExecution { get; set; }
    [JsonPropertyName("HealthStatus")]
    public required string HealthStatus { get; set; }
    [JsonPropertyName("Calls")]
    public List<CallListResult>? Calls { get; set; }
    [JsonPropertyName("Logs")]
    public List<LogEntry>? Logs { get; set; }
}

public class WorkerTriggerUpdateResult
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

public class WorkerLogsResults
{
    [JsonPropertyName("WorkerName")]
    public required string WorkerName { get; set; }
    [JsonPropertyName("Logs")]
    public required List<LogEntry> Logs { get; set; }
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

public class WorkerClearLogsResults
{
    [JsonPropertyName("WorkerName")]
    public required string WorkerName { get; set; }
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

public class WorkerLogCountsResults
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
    [JsonPropertyName("Counts")]
    public required Dictionary<string, Dictionary<string, int>> Counts { get; set; }
}
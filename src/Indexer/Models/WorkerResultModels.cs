using System.Text.Json.Serialization;

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
}

public class WorkerTriggerUpdateResult
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}
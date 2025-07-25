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
}

public class CallListResults
{
    [JsonPropertyName("Calls")]
    public required List<CallListResult> Calls { get; set; }
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

public class CallListResult
{
    [JsonPropertyName("CallConfig")]
    public required CallConfig CallConfig { get; set; }
    [JsonPropertyName("IsActive")]
    public required bool IsActive { get; set; }
    [JsonPropertyName("IsExecuting")]
    public required bool IsExecuting { get; set; }
    [JsonPropertyName("LastExecution")]
    public required DateTime? LastExecution { get; set; }
    [JsonPropertyName("LastSuccessfulExecution")]
    public required DateTime? LastSuccessfulExecution { get; set; }
    [JsonPropertyName("HealthStatus")]
    public required string HealthStatus { get; set; }
}

public class WorkerStopResult
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

public class WorkerStartResult
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

public class WorkerTriggerUpdateResult
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

public class WorkerReloadConfigResult
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

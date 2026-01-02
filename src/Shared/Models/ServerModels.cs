using System.Text.Json.Serialization;

namespace Shared.Models;

public class ServerGetModelsResult : SuccesMessageBaseModel
{
    [JsonPropertyName("Models")]
    public string[]? Models { get; set; }    
}

public class ServerGetStatsResult : SuccesMessageBaseModel
{
    [JsonPropertyName("SizeInBytes")]
    public long? SizeInBytes { get; set; }
    [JsonPropertyName("MaxElementCount")]
    public long? MaxElementCount { get; set; }
    [JsonPropertyName("ElementCount")]
    public long? ElementCount { get; set; }
    [JsonPropertyName("EmbeddingsCount")]
    public long? EmbeddingsCount { get; set; }
    [JsonPropertyName("EntityCount")]
    public long? EntityCount { get; set; }
    [JsonPropertyName("QueryCacheUtilization")]
    public long? QueryCacheUtilization { get; set; }
}
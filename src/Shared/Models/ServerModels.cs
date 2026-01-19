using System.Text.Json.Serialization;

namespace Shared.Models;

public class ServerGetModelsResult : SuccesMessageBaseModel
{
    [JsonPropertyName("Models")]
    public string[]? Models { get; set; }    
}

public class ServerGetStatsResult : SuccesMessageBaseModel
{
    [JsonPropertyName("EmbeddingCacheUtilization")]
    public long? EmbeddingCacheUtilization { get; set; }
    [JsonPropertyName("EmbeddingCacheMaxElementCount")]
    public long? EmbeddingCacheMaxElementCount { get; set; }
    [JsonPropertyName("ElementCount")]
    public long? EmbeddingCacheElementCount { get; set; }
    [JsonPropertyName("EmbeddingsCount")]
    public long? EmbeddingsCount { get; set; }
    [JsonPropertyName("EntityCount")]
    public long? EntityCount { get; set; }
    [JsonPropertyName("QueryCacheElementCount")]
    public long? QueryCacheElementCount { get; set; }
    [JsonPropertyName("QueryCacheMaxElementCountAll")]
    public long? QueryCacheMaxElementCountAll { get; set; }
    [JsonPropertyName("QueryCacheMaxElementCountLoadedSearchdomainsOnly")]
    public long? QueryCacheMaxElementCountLoadedSearchdomainsOnly { get; set; }
    [JsonPropertyName("QueryCacheUtilization")]
    public long? QueryCacheUtilization { get; set; }
    [JsonPropertyName("DatabaseTotalSize")]
    public long? DatabaseTotalSize { get; set; }
    [JsonPropertyName("RamTotalSize")]
    public long? RamTotalSize { get; set; }
}
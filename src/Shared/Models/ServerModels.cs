using System.Text.Json.Serialization;

namespace Shared.Models;

public class ServerGetModelsResult : SuccesMessageBaseModel
{
    [JsonPropertyName("Models")]
    public string[]? Models { get; set; }    
}

public class ServerGetEmbeddingCacheSizeResult : SuccesMessageBaseModel
{
    [JsonPropertyName("SizeInBytes")]
    public required long? SizeInBytes { get; set; }
    [JsonPropertyName("MaxElementCount")]
    public required long? MaxElementCount { get; set; }
    [JsonPropertyName("ElementCount")]
    public required long? ElementCount { get; set; }
    [JsonPropertyName("EmbeddingsCount")]
    public required long? EmbeddingsCount { get; set; }    
}
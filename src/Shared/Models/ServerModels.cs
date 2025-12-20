using System.Text.Json.Serialization;

namespace Shared.Models;

public class ServerGetModelsResult
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }

    [JsonPropertyName("Message")]
    public string? Message { get; set; }

    [JsonPropertyName("Models")]
    public string[]? Models { get; set; }    
}
using System.Text.Json.Serialization;

namespace Shared.Models;

public class SuccesMessageBaseModel
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
    [JsonPropertyName("Message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }
}
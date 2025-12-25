using System.Text.Json.Serialization;

namespace Shared.Models;

public class ServerGetModelsResult : SuccesMessageBaseModel
{
    [JsonPropertyName("Models")]
    public string[]? Models { get; set; }    
}
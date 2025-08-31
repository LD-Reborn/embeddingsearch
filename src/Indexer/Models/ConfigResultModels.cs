using System.Text.Json.Serialization;

namespace Indexer.Models;

public class ConfigReloadResult
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

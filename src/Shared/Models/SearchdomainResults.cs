using System.Text.Json.Serialization;

namespace Shared.Models;

public class SearchdomainListResults
{
    [JsonPropertyName("Searchdomains")] // Otherwise the api returns {"searchdomains": [...]} and the client requires {"Searchdomains": [...]}
    public required List<string> Searchdomains { get; set; }
}

public class SearchdomainCreateResults
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }

    [JsonPropertyName("Id")]
    public int? Id { get; set; }
}

public class SearchdomainUpdateResults
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

public class SearchdomainDeleteResults
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
    [JsonPropertyName("DeletedEntities")]
    public required int DeletedEntities { get; set; }
}

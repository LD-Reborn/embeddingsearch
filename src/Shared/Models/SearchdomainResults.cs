using System.Text.Json.Serialization;

namespace Shared.Models;

public class SearchdomainListResults
{
    [JsonPropertyName("Searchdomains")] // Otherwise the api returns {"searchdomains": [...]} and the client requires {"Searchdomains": [...]}
    public required List<string> Searchdomains { get; set; }

    [JsonPropertyName("Message")]
    public string? Message { get; set; }
}

public class SearchdomainCreateResults : SuccesMessageBaseModel
{
    [JsonPropertyName("Id")]
    public int? Id { get; set; }
}

public class SearchdomainUpdateResults : SuccesMessageBaseModel {}

public class SearchdomainDeleteResults : SuccesMessageBaseModel
{
    [JsonPropertyName("DeletedEntities")]
    public required int DeletedEntities { get; set; }
}

public class SearchdomainSearchesResults : SuccesMessageBaseModel
{
    [JsonPropertyName("Searches")]
    public required Dictionary<string, DateTimedSearchResult> Searches { get; set; }
}

public class SearchdomainDeleteSearchResult : SuccesMessageBaseModel {}

public class SearchdomainUpdateSearchResult : SuccesMessageBaseModel {}

public class SearchdomainSettingsResults : SuccesMessageBaseModel
{
    [JsonPropertyName("Settings")]
    public required SearchdomainSettings? Settings { get; set; }
}

public class SearchdomainSearchCacheSizeResults : SuccesMessageBaseModel
{
    [JsonPropertyName("QueryCacheSizeBytes")]
    public required long? QueryCacheSizeBytes { get; set; }
}

public class SearchdomainInvalidateCacheResults : SuccesMessageBaseModel {}

public class SearchdomainGetDatabaseSizeResult : SuccesMessageBaseModel
{
    [JsonPropertyName("SearchdomainDatabaseSizeBytes")]
    public required long? SearchdomainDatabaseSizeBytes { get; set; }    
}


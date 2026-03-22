using System.Text.Json.Serialization;
using Shared;

namespace Shared.Models;

public class SearchdomainListResults
{
    [JsonPropertyName("Searchdomains")] // Otherwise the api returns {"searchdomains": [...]} and the client requires {"Searchdomains": [...]}
    public required List<string> Searchdomains { get; set; }

    [JsonPropertyName("Message")]
    public string? Message { get; set; }
}

public class SearchdomainCreateResults : SuccessMessageBaseModel
{
    [JsonPropertyName("Id")]
    public int? Id { get; set; }
}

public class SearchdomainUpdateResults : SuccessMessageBaseModel {}

public class SearchdomainDeleteResults : SuccessMessageBaseModel
{
    [JsonPropertyName("DeletedEntities")]
    public required int DeletedEntities { get; set; }
}

public class SearchdomainQueriesResults : SuccessMessageBaseModel
{
    [JsonPropertyName("Searches")]
    public required Dictionary<string, DateTimedSearchResult> Searches { get; set; }
}

public class SearchdomainDeleteSearchResult : SuccessMessageBaseModel {}

public class SearchdomainUpdateSearchResult : SuccessMessageBaseModel {}

public class SearchdomainSettingsResults : SuccessMessageBaseModel
{
    [JsonPropertyName("Settings")]
    public required SearchdomainSettings? Settings { get; set; }
}

public class SearchdomainQueryCacheSizeResults : SuccessMessageBaseModel
{
    [JsonPropertyName("ElementCount")]
    public required int? ElementCount { get; set; }
    [JsonPropertyName("ElementMaxCount")]
    public required int? ElementMaxCount { get; set; }
    [JsonPropertyName("SizeBytes")]
    public required long? SizeBytes { get; set; }
}

public class SearchdomainInvalidateCacheResults : SuccessMessageBaseModel {}

public class SearchdomainGetDatabaseSizeResult : SuccessMessageBaseModel
{
    [JsonPropertyName("SearchdomainDatabaseSizeBytes")]
    public required long? SearchdomainDatabaseSizeBytes { get; set; }    
}


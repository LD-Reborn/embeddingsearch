using System.Configuration;
using ElmahCore;
using Shared.Models;

namespace Server.Models;

public class EmbeddingSearchOptions : IApiKeyOptions
{
    public required ConnectionStringsOptions ConnectionStrings { get; set; }
    public ElmahOptions? Elmah { get; set; }
    public required Dictionary<string, AiProviderOptions> AiProviders { get; set; }
    public required SimpleAuthOptions SimpleAuth { get; set; }
    public required CacheOptions Cache { get; set; }
    public required bool UseHttpsRedirection { get; set; }
    public int? MaxRequestBodySize { get; set; }
    public string[]? ApiKeys { get; set; }
}

public class SimpleAuthOptions
{
    public List<SimpleUser> Users { get; set; } = [];
}

public class SimpleUser
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string[] Roles { get; set; } = [];
}

public class ConnectionStringsOptions
{
    public required string SQL { get; set; }
    public string? Cache { get; set; }
}

public class CacheOptions
{
    public required long CacheTopN { get; set; }
    public bool StoreEmbeddingCache { get; set; } = false;
    public int? StoreTopN { get; set; }
}
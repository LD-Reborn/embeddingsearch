using System.Configuration;
using ElmahCore;
using Shared.Models;

namespace Server.Models;

public class EmbeddingSearchOptions : ApiKeyOptions
{
    public required ConnectionStringsOptions ConnectionStrings { get; set; }
    public ElmahOptions? Elmah { get; set; }
    public required Dictionary<string, AiProvider> AiProviders { get; set; }
    public required SimpleAuthOptions SimpleAuth { get; set; }
    public required CacheOptions Cache { get; set; }
    public required bool UseHttpsRedirection { get; set; }
}

public class AiProvider
{
    public required string Handler { get; set; }
    public required string BaseURL { get; set; }
    public string? ApiKey { get; set; }
    public required string[] Allowlist { get; set; }
    public required string[] Denylist { get; set; }
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
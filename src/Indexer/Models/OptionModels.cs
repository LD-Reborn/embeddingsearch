using Shared.Models;
namespace Indexer.Models;

public class IndexerOptions : IApiKeyOptions
{
    public required WorkerConfig[] Workers { get; set; }
    public required ServerOptions Server { get; set;}
    public required string PythonRuntime { get; set; } = "libpython3.13.so";
    public string[]? ApiKeys { get; set; }
    public required Dictionary<string, AiProviderOptions> AiProviders { get; set; }
    public string? DefaultVisionModel { get; set; }
    public SimpleAuthOptions? SimpleAuth { get; set; }
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

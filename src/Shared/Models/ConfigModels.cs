namespace Shared.Models;

public interface IAiProviderCollectionOptions
{
    public Dictionary<string, AiProviderOptions> AiProviders { get; set; }
}

public class AiProviderCollectionOptions : IAiProviderCollectionOptions
{
    public required Dictionary<string, AiProviderOptions> AiProviders { get; set; }
}

public class AiProviderOptions
{
    public required string Handler { get; set; }
    public required string BaseURL { get; set; }
    public string? ApiKey { get; set; }
    public required string[] Allowlist { get; set; }
    public required string[] Denylist { get; set; }
}
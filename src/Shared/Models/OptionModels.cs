namespace Shared.Models;

public interface IApiKeyOptions
{
    public string[]? ApiKeys { get; set; }
}

public class ApiKeyOptions : IApiKeyOptions
{
    public string[]? ApiKeys { get; set; }
}

public class ServerOptions
{
    public required string BaseUri { get; set; }
    public string? ApiKey { get; set; }
    public string? Searchdomain { get; set; }
}
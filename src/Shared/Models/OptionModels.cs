namespace Shared.Models;

public class ApiKeyOptions
{
    public string[]? ApiKeys { get; set; }
}

public class ServerOptions
{
    public required string BaseUri { get; set; }
    public string? ApiKey { get; set; }
    public string? Searchdomain { get; set; }
}
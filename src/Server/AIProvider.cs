
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Server.Exceptions;

namespace Server;

public class AIProvider
{
    private readonly ILogger<AIProvider> _logger;
    private readonly IConfiguration _configuration;
    public AIProvidersConfiguration aIProvidersConfiguration;

    public AIProvider(ILogger<AIProvider> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        AIProvidersConfiguration? retrievedAiProvidersConfiguration = _configuration
            .GetSection("Embeddingsearch")
            .Get<AIProvidersConfiguration>();
        if (retrievedAiProvidersConfiguration is null)
        {
            _logger.LogCritical("Unable to build AIProvidersConfiguration. Please check your configuration.");
            throw new ServerConfigurationException("Unable to build AIProvidersConfiguration. Please check your configuration.");
        }
        else
        {
            aIProvidersConfiguration = retrievedAiProvidersConfiguration;
        }
    }

    public float[] GenerateEmbeddings(string modelUri, string[] input)
    {
        Uri uri = new(modelUri);
        string provider = uri.Scheme;
        string model = uri.AbsolutePath;
        AIProviderConfiguration? aIProvider = aIProvidersConfiguration.AiProviders
            .FirstOrDefault(x => String.Equals(x.Key.ToLower(), provider.ToLower()))
            .Value;
        if (aIProvider is null)
        {
            _logger.LogError("Model provider {provider} not found in configuration. Requested model: {modelUri}", [provider, modelUri]);
            throw new ServerConfigurationException($"Model provider {provider} not found in configuration. Requested model: {modelUri}");
        }
        using var httpClient = new HttpClient();

        string embeddingsJsonPath = "";
        Uri baseUri = new(aIProvider.BaseURL);
        Uri requestUri;
        IEmbedRequestBody embedRequest;
        string[][] requestHeaders = [];
        switch (aIProvider.Handler)
        {
            case "ollama":
                embeddingsJsonPath = "$.embeddings[*]";
                requestUri = new Uri(baseUri, "/api/embed");
                embedRequest = new OllamaEmbedRequestBody()
                {
                    input = input,
                    model = model
                };
                break;
            case "openai":
                embeddingsJsonPath = "$.data[*].embedding";
                requestUri = new Uri(baseUri, "/v1/embeddings");
                embedRequest = new OpenAIEmbedRequestBody()
                {
                    input = input,
                    model = model
                };
                if (aIProvider.ApiKey is not null)
                {
                    requestHeaders = [
                        ["Authorization", $"Bearer {aIProvider.ApiKey}"]
                    ];
                }
                break;
            default:
                _logger.LogError("Unknown handler {aIProvider.Handler} in AiProvider {provider}.", [aIProvider.Handler, provider]);
                throw new ServerConfigurationException($"Unknown handler {aIProvider.Handler} in AiProvider {provider}.");
        }
        var requestContent = new StringContent(
            JsonConvert.SerializeObject(embedRequest),
            UnicodeEncoding.UTF8,
            "application/json"
        );

        var request = new HttpRequestMessage()
        {
            RequestUri = requestUri,
            Method = HttpMethod.Post,
            Content = requestContent
        };
        
        foreach (var header in requestHeaders)
        {
            request.Headers.Add(header[0], header[1]);
        }
        HttpResponseMessage response = httpClient.PostAsync(requestUri, requestContent).Result;
        string responseContent = response.Content.ReadAsStringAsync().Result;
        try
        {
            JObject responseContentJson = JObject.Parse(responseContent);
            JToken? responseContentTokens = responseContentJson.SelectToken(embeddingsJsonPath);
            if (responseContentTokens is null)
            {
                _logger.LogError("Unable to select tokens using JSONPath {embeddingsJsonPath} for string: {responseContent}.", [embeddingsJsonPath, responseContent]);
                throw new Exception($"Unable to select tokens using JSONPath {embeddingsJsonPath} for string: {responseContent}."); // TODO add proper exception
            }
            return [.. responseContentTokens.Values<float>()];
        }
        catch (Exception ex)
        {
            _logger.LogError("Unable to parse the response to valid embeddings. {ex.Message}", [ex.Message]);
            throw;
        }
    }
}

public class AIProvidersConfiguration
{
    public required Dictionary<string, AIProviderConfiguration> AiProviders { get; set; }
}

public class AIProviderConfiguration
{
    public required string Handler { get; set; }
    public required string BaseURL { get; set; }
    public string? ApiKey { get; set; }
}
public interface IEmbedRequestBody { }

public class OllamaEmbedRequestBody : IEmbedRequestBody
{
    public required string model { get; set; }
    public required string[] input { get; set; }
}

public class OpenAIEmbedRequestBody : IEmbedRequestBody
{
    public required string model { get; set; }
    public required string[] input { get; set; }
}
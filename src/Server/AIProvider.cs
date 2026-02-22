
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Server.Exceptions;
using Server.Models;

namespace Server;

public class AIProvider
{
    private readonly ILogger<AIProvider> _logger;
    private readonly EmbeddingSearchOptions _configuration;
    public Dictionary<string, AiProvider> AiProvidersConfiguration;

    public AIProvider(ILogger<AIProvider> logger, IOptions<EmbeddingSearchOptions> configuration)
    {
        _logger = logger;
        _configuration = configuration.Value;
        Dictionary<string, AiProvider>? retrievedAiProvidersConfiguration = _configuration.AiProviders;
        if (retrievedAiProvidersConfiguration is null)
        {
            _logger.LogCritical("Unable to build AIProvidersConfiguration. Please check your configuration.");
            throw new ServerConfigurationException("Unable to build AIProvidersConfiguration. Please check your configuration.");
        }
        else
        {
            AiProvidersConfiguration = retrievedAiProvidersConfiguration;
        }
    }

    public float[] GenerateEmbeddings(string modelUri, string input)
    {
        return [.. GenerateEmbeddings(modelUri, [input]).First()];
    }

    public IEnumerable<float[]> GenerateEmbeddings(string modelUri, string[] input)
    {
        Uri uri = new(modelUri);
        string provider = uri.Scheme;
        string model = uri.AbsolutePath;
        AiProvider? aIProvider = AiProvidersConfiguration
            .FirstOrDefault(x => string.Equals(x.Key.ToLower(), provider.ToLower()))
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
            List<JToken>? responseContentTokens = [.. responseContentJson.SelectTokens(embeddingsJsonPath)];
            if (responseContentTokens is null || responseContentTokens.Count == 0)
            {
                if (responseContentJson.TryGetValue("error", out JToken? errorMessageJson) && errorMessageJson is not null)
                {
                    string errorMessage = errorMessageJson.Value<string>() ?? "";
                    _logger.LogError("Unable to retrieve embeddings due to error: {errorMessage}", [errorMessage]);
                    throw new Exception($"Unable to retrieve embeddings due to error: {errorMessage}");
                    
                } else
                {
                    _logger.LogError("Unable to select tokens using JSONPath {embeddingsJsonPath} for string: {responseContent}.", [embeddingsJsonPath, responseContent]);
                    throw new JSONPathSelectionException(embeddingsJsonPath, responseContent);
                }
            }
            return [.. responseContentTokens.Select(token => token.ToObject<float[]>() ?? throw new Exception("Unable to cast embeddings response to float[]"))];
        }
        catch (Exception ex)
        {
            _logger.LogError("Unable to parse the response to valid embeddings. {ex.Message}", [ex.Message]);
            throw;
        }
    }

    public string[] GetModels()
    {
        var aIProviders = AiProvidersConfiguration;
        List<string> results = [];
        foreach (KeyValuePair<string, AiProvider> aIProviderKV in aIProviders)
        {
            string aIProviderName = aIProviderKV.Key;
            AiProvider aIProvider = aIProviderKV.Value;

            using var httpClient = new HttpClient();

            string modelNameJsonPath = "";
            Uri baseUri = new(aIProvider.BaseURL);
            Uri requestUri;
            string[][] requestHeaders = [];
            switch (aIProvider.Handler)
            {
                case "ollama":
                    modelNameJsonPath = "$.models[*].name";
                    requestUri = new Uri(baseUri, "/api/tags");
                    break;
                case "openai":
                    modelNameJsonPath = "$.data[*].id";
                    requestUri = new Uri(baseUri, "/v1/models");
                    if (aIProvider.ApiKey is not null)
                    {
                        requestHeaders = [
                            ["Authorization", $"Bearer {aIProvider.ApiKey}"]
                        ];
                    }
                    break;
                default:
                    _logger.LogError("Unknown handler {aIProvider.Handler} in AiProvider {provider}.", [aIProvider.Handler, aIProvider]);
                    throw new ServerConfigurationException($"Unknown handler {aIProvider.Handler} in AiProvider {aIProvider}.");
            }

            var request = new HttpRequestMessage()
            {
                RequestUri = requestUri,
                Method = HttpMethod.Post
            };
            
            foreach (var header in requestHeaders)
            {
                request.Headers.Add(header[0], header[1]);
            }
            HttpResponseMessage response = httpClient.GetAsync(requestUri).Result;
            string responseContent = response.Content.ReadAsStringAsync().Result;
            try
            {
                JObject responseContentJson = JObject.Parse(responseContent);
                IEnumerable<JToken>? responseContentTokens = responseContentJson.SelectTokens(modelNameJsonPath);
                if (responseContentTokens is null)
                {
                    _logger.LogError("Unable to select tokens using JSONPath {modelNameJsonPath} for string: {responseContent}.", [modelNameJsonPath, responseContent]);
                    throw new JSONPathSelectionException(modelNameJsonPath, responseContent);
                }
                IEnumerable<string?> aIProviderResult = responseContentTokens.Values<string>();
                foreach (string? result in aIProviderResult)
                {
                    if (result is null) continue;
                    bool isInAllowList = ElementMatchesAnyRegexInList(result, aIProvider.Allowlist);
                    bool isInDenyList =  ElementMatchesAnyRegexInList(result, aIProvider.Denylist);
                    if (isInAllowList && !isInDenyList)
                    {
                        results.Add(aIProviderName + ":" + result);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Unable to parse the response to valid models. {ex.Message}", [ex.Message]);
                throw;
            }
        }
        return [.. results];
    }

    private static bool ElementMatchesAnyRegexInList(string element, string[] list)
    {
        return list?.Any(pattern => pattern != null && Regex.IsMatch(element, pattern)) ?? false;
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
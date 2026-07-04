
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Exceptions;
using Shared.Models;

namespace Shared.Services;

public class AIProviderService
{
    private readonly ILogger<AIProviderService> _logger;
    private readonly Dictionary<string, AiProviderOptions> _configuration;
    public Dictionary<string, AiProviderOptions> AiProvidersConfiguration;

    public AIProviderService(ILogger<AIProviderService> logger, IOptions<AiProviderCollectionOptions> configuration)
    {
        _logger = logger;
        _configuration = configuration.Value.AiProviders;
        AiProvidersConfiguration = _configuration;
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
        AiProviderOptions? aIProvider = TryGetAiProvider(uri);
        if (aIProvider is null)
        {
            _logger.LogError("Model provider {provider} not found in configuration. Requested model: {modelUri}", [provider, modelUri]);
            throw new ConfigurationException($"Model provider {provider} not found in configuration. Requested model: {modelUri}");
        }
        using var httpClient = new HttpClient();

        Uri baseUri = new(aIProvider.BaseURL);
        IEmbedRequestBody embedRequest;
        (string embeddingsJsonPath, Uri requestUri, string[][] requestHeaders) = GetRequestInformation(AiProviderRequestType.Embeddings, aIProvider);
        switch (aIProvider.Handler)
        {
            case "ollama":
                embedRequest = new OllamaEmbedRequestBody()
                {
                    input = input,
                    model = model
                };
                break;
            case "openai":
                embedRequest = new OpenAIEmbedRequestBody()
                {
                    input = input,
                    model = model
                };
                break;
            default:
                _logger.LogError("Unknown handler {aIProvider.Handler} in AiProvider {provider}.", [aIProvider.Handler, provider]);
                throw new ConfigurationException($"Unknown handler {aIProvider.Handler} in AiProvider {provider}.");
        }
        var requestContent = new StringContent(
            JsonConvert.SerializeObject(embedRequest),
            Encoding.UTF8,
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
        HttpResponseMessage response;
        try
        {
            response = httpClient.PostAsync(requestUri, requestContent).Result;
        } catch (Exception e)
        {
            throw new AggregateException("Unable to retrieve embeddings", e);
        }
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

    public AiProviderOptions? TryGetAiProvider(Uri modelUri)
    {
        string provider = modelUri.Scheme;
        string model = modelUri.AbsolutePath;
        return AiProvidersConfiguration
            .FirstOrDefault(x => string.Equals(x.Key.ToLower(), provider.ToLower()))
            .Value;
    }

    public string[] GetModels()
    {
        var aIProviders = AiProvidersConfiguration;
        List<string> results = [];
        foreach (KeyValuePair<string, AiProviderOptions> aIProviderKV in aIProviders)
        {
            string aIProviderName = aIProviderKV.Key;
            AiProviderOptions aIProvider = aIProviderKV.Value;

            using var httpClient = new HttpClient();

            (string modelNameJsonPath, Uri requestUri, string[][] requestHeaders) = GetRequestInformation(AiProviderRequestType.Models, aIProvider);

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
                _logger.LogError("Unable to list models for AIProvider {aIProviderName}. {ex.Message}", [aIProviderName, ex.Message]);
                throw new AggregateException($"Unable to list models for AIProvider {aIProviderName}. {ex.Message}", ex);
            }
        }
        return [.. results];
    }

    public string GenerateResponse(
        string model,
        string prompt,
        string[]? images = null,
        string? format = null,
        string? system = null,
        bool think = false)
    {
        AiProviderOptions? aIProvider = null;
        Uri modelUri = new(model);
        string provider = new Uri(model).Scheme;
        model = modelUri.AbsolutePath;
        foreach (KeyValuePair<string, AiProviderOptions> kvp in AiProvidersConfiguration)
        {
            string handler = kvp.Key;
            AiProviderOptions aiProvider = kvp.Value;
            if (handler == provider)
            {
                aIProvider = aiProvider;
            }
        }
        if (aIProvider is null)
        {
            throw new UnknownProviderException(provider, model);
        }
        var results = new List<string>();
        var (jsonPath, requestUri, requestHeaders) = GetRequestInformation(AiProviderRequestType.Generate, aIProvider);

        IGenerateRequestBody requestBody;
        if (aIProvider.Handler == "ollama")
        {
            requestBody = new OllamaGenerateRequestBody
            {
                Model = model,
                Prompt = prompt,
                Images = images,
                Format = format,
                System = system,
                Stream = false,
                Think = think
            };
        }
        else if (aIProvider.Handler == "openai")
        {
            var messages = new List<OpenAIMessage>();
            if (!string.IsNullOrEmpty(system))
                messages.Add(new OpenAIMessage { role = "system", content = system });
            if (images is { Length: > 0 })
            {
                var imageContent = new List<OpenAIContent>();
                foreach (var img in images)
                {
                    imageContent.Add(new OpenAIContent
                    {
                        type = "image_url",
                        image_url = new OpenAIImageUrl { url = $"data:image/jpeg;base64,{img}" }
                    });
                }
                imageContent.Add(new OpenAIContent { type = "text", text = prompt });
                messages.Add(new OpenAIMessage { role = "user", content = imageContent });
            }
            else
            {
                messages.Add(new OpenAIMessage { role = "user", content = prompt });
            }

            requestBody = new OpenAIGenerateRequestBody
            {
                model = model,
                messages = [.. messages],
                stream = false,
                response_format = !string.IsNullOrEmpty(format) && format.ToLowerInvariant() == "json"
                    ? new OpenAIResponseFormat { type = "json_object" }
                    : null
            };
        }
        else
        {
            _logger.LogError("Unsupported handler '{handler}' for generate request.", aIProvider.Handler);
            throw new UnsupportedConfigurationException(AiProviderRequestType.Generate, aIProvider.Handler);
        }

        string requestBodyJson = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = content
        };

        foreach (var header in requestHeaders)
        {
            request.Headers.Add(header[0], header[1]);
        }

        using var httpClient = new HttpClient();
        HttpResponseMessage response = httpClient.SendAsync(request).GetAwaiter().GetResult();
        string responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("AI provider '{provider}' returned status {status}: {responseContent}", 
                provider, response.StatusCode, responseContent);
            throw new Exception($"AI provider '{provider}' returned status {response.StatusCode}: {responseContent}");
        }

        try
        {
            JObject responseJson = JObject.Parse(responseContent);

            string? resultText = aIProvider.Handler switch
            {
                "ollama" => responseJson.SelectToken("$.response")?.ToString(),
                "openai" => responseJson.SelectToken("$.choices[0].message.content")?.ToString(),
                _ => null
            };

            if (!string.IsNullOrEmpty(resultText))
            {
                results.Add(resultText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse generate response from '{provider}'.", provider);
            throw;
        }

        return string.Join("\n", results);
    }

    public static (string JsonPath, Uri RequestUri, string[][] Headers) GetRequestInformation(AiProviderRequestType requestType, AiProviderOptions aIProvider)
    {
        string[][] headers = [];
        string[][] bearerBasedHeaders = aIProvider.ApiKey is null ? [] : [["Authorization", $"Bearer {aIProvider.ApiKey}"]];
        Uri baseUri = new(aIProvider.BaseURL);
        Dictionary<AiProviderRequestType, Dictionary<string, (string JsonPath, string UriAppendix, string[][] headers)>> values = new()
        {
            {AiProviderRequestType.Models, new()
                {
                    {"ollama", ("$.models[*].name", "/api/tags", [])},
                    {"openai", ("$.data[*].id", "/v1/models", bearerBasedHeaders)}
                }},
            {AiProviderRequestType.Embeddings, new()
                {
                    {"ollama", ("$.embeddings[*]", "/api/embed", [])},
                    {"openai", ("$.data[*].embedding", "/v1/embeddings", bearerBasedHeaders)}
                }
            },
            {AiProviderRequestType.Generate, new()
                {
                    {"ollama", ("$.response", "/api/generate", [])},
                    {"openai", ("$.choices[0].message.content", "/v1/chat/completions", bearerBasedHeaders)}
                }
            }
            
        };

        try
        {
            (string JsonPath, string UriAppendix, string[][] headers) value = values
                .First(byRequestType => byRequestType.Key == requestType).Value
                .First(x => x.Key == aIProvider.Handler).Value;

            (string JsonPath, Uri RequestUri, string[][] Headers) returnValue = (
                value.JsonPath,
                new Uri(new Uri(aIProvider.BaseURL), value.UriAppendix),
                value.headers);
            return returnValue;
        }
        catch (ArgumentNullException)
        {
            throw new UnsupportedConfigurationException(requestType, aIProvider.Handler);
        }
    }

    private static bool ElementMatchesAnyRegexInList(string element, string[] list)
    {
        return list?.Any(pattern => pattern != null && Regex.IsMatch(element, pattern)) ?? false;
    }
}

public enum AiProviderRequestType
{
    Models,
    Generate,
    Embeddings,
    Rerank
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

public interface IGenerateRequestBody { }

public class OllamaGenerateRequestBody : IGenerateRequestBody
{
    [JsonProperty(PropertyName = "model")]
    public required string Model { get; set; }
    [JsonProperty(PropertyName = "prompt")]
    public required string Prompt { get; set; }
    [JsonProperty(PropertyName = "images", NullValueHandling = NullValueHandling.Ignore)]
    public string[]? Images { get; set; }
    [JsonProperty(PropertyName = "format", NullValueHandling = NullValueHandling.Ignore)]
    public string? Format { get; set; }
    [JsonProperty(PropertyName = "system", NullValueHandling = NullValueHandling.Ignore)]
    public string? System { get; set; }
    [JsonProperty(PropertyName = "stream")]
    public bool Stream { get; set; }
    [JsonProperty(PropertyName = "think", NullValueHandling = NullValueHandling.Ignore)]
    public bool Think { get; set; }
}

public class OpenAIGenerateRequestBody : IGenerateRequestBody
{
    public required string model { get; set; }
    public required OpenAIMessage[] messages { get; set; }
    public bool stream { get; set; }
    public OpenAIResponseFormat? response_format { get; set; }
}

public class OpenAIMessage
{
    public required string role { get; set; }
    public object? content { get; set; } // string or List<OpenAIContent>
}

public class OpenAIContent
{
    public required string type { get; set; }
    public string? text { get; set; }
    public OpenAIImageUrl? image_url { get; set; }
}

public class OpenAIImageUrl
{
    public required string url { get; set; }
}

public class OpenAIResponseFormat
{
    public required string type { get; set; }
}
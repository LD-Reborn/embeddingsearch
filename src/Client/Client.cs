using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Reflection.Metadata.Ecma335;
using Shared.Models;
using System.Net;

namespace Client;

public class Client
{
        public string baseUri;
        public string apiKey;
        public string searchdomain;

        public Client(string baseUri, string apiKey = "", string searchdomain = "")
        {
            this.baseUri = baseUri;
            this.apiKey = apiKey;
            this.searchdomain = searchdomain;
        }

        public Client(IConfiguration configuration)
        {
            string? baseUri = configuration.GetSection("Embeddingsearch").GetValue<string>("BaseUri");
            string? apiKey = configuration.GetSection("Embeddingsearch").GetValue<string>("ApiKey");
            string? searchdomain = configuration.GetSection("Embeddingsearch").GetValue<string>("Searchdomain");
            this.baseUri = baseUri ?? "";
            this.apiKey = apiKey ?? "";
            this.searchdomain = searchdomain ?? "";
        }

        public async Task<EntityListResults> EntityListAsync(bool returnEmbeddings = false)
        {
            return await EntityListAsync(searchdomain, returnEmbeddings);
        }

        public async Task<EntityListResults> EntityListAsync(string searchdomain, bool returnEmbeddings = false)
        {
            var url = $"{baseUri}/Entities?searchdomain={HttpUtility.UrlEncode(searchdomain)}&returnEmbeddings={HttpUtility.UrlEncode(returnEmbeddings.ToString())}";
            return await FetchUrlAndProcessJson<EntityListResults>(HttpMethod.Get, url);
        }

        public async Task<EntityIndexResult> EntityIndexAsync(List<JSONEntity> jsonEntity)
        {
            return await EntityIndexAsync(JsonSerializer.Serialize(jsonEntity));
        }

        public async Task<EntityIndexResult> EntityIndexAsync(string jsonEntity)
        {
            var content = new StringContent(jsonEntity, Encoding.UTF8, "application/json");
            return await FetchUrlAndProcessJson<EntityIndexResult>(HttpMethod.Put, GetUrl($"{baseUri}", "Entities", []), content);
        }

        public async Task<EntityDeleteResults> EntityDeleteAsync(string entityName)
        {
            return await EntityDeleteAsync(searchdomain, entityName);
        }

        public async Task<EntityDeleteResults> EntityDeleteAsync(string searchdomain, string entityName)
        {
            var url = $"{baseUri}/Entity?apiKey={HttpUtility.UrlEncode(apiKey)}&searchdomain={HttpUtility.UrlEncode(searchdomain)}&entity={HttpUtility.UrlEncode(entityName)}";
            return await FetchUrlAndProcessJson<EntityDeleteResults>(HttpMethod.Delete, url);
        }

        public async Task<SearchdomainListResults> SearchdomainListAsync()
        {
            return await FetchUrlAndProcessJson<SearchdomainListResults>(HttpMethod.Get, GetUrl($"{baseUri}", "Searchdomains", []));
        }

        public async Task<SearchdomainCreateResults> SearchdomainCreateAsync()
        {
            return await SearchdomainCreateAsync(searchdomain);
        }

        public async Task<SearchdomainCreateResults> SearchdomainCreateAsync(string searchdomain, SearchdomainSettings searchdomainSettings = new())
        {
            return await FetchUrlAndProcessJson<SearchdomainCreateResults>(HttpMethod.Post, GetUrl($"{baseUri}", "Searchdomain", new Dictionary<string, string>()
            {
                {"searchdomain", searchdomain}
            }), new StringContent(JsonSerializer.Serialize(searchdomainSettings), Encoding.UTF8, "application/json"));
        }

        public async Task<SearchdomainDeleteResults> SearchdomainDeleteAsync()
        {
            return await SearchdomainDeleteAsync(searchdomain);
        }

        public async Task<SearchdomainDeleteResults> SearchdomainDeleteAsync(string searchdomain)
        {
            return await FetchUrlAndProcessJson<SearchdomainDeleteResults>(HttpMethod.Delete, GetUrl($"{baseUri}", "Searchdomain", new Dictionary<string, string>()
            {
                {"searchdomain", searchdomain}
            }));
        }

        public async Task<SearchdomainUpdateResults> SearchdomainUpdateAsync(string newName, string settings = "{}")
        {
            SearchdomainUpdateResults updateResults = await SearchdomainUpdateAsync(searchdomain, newName, settings);
            searchdomain = newName;
            return updateResults;
        }

        public async Task<SearchdomainUpdateResults> SearchdomainUpdateAsync(string searchdomain, string newName, SearchdomainSettings settings = new())
        {
             return await SearchdomainUpdateAsync(searchdomain, newName, JsonSerializer.Serialize(settings));
        }

        public async Task<SearchdomainUpdateResults> SearchdomainUpdateAsync(string searchdomain, string newName, string settings = "{}")
        {
            return await FetchUrlAndProcessJson<SearchdomainUpdateResults>(HttpMethod.Put, GetUrl($"{baseUri}", "Searchdomain", new Dictionary<string, string>()
            {
                {"searchdomain", searchdomain},
                {"newName", newName}
            }), new StringContent(settings, Encoding.UTF8, "application/json"));
        }

        public async Task<SearchdomainSearchesResults> SearchdomainGetQueriesAsync(string searchdomain)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain}
            };
            return await FetchUrlAndProcessJson<SearchdomainSearchesResults>(HttpMethod.Get, GetUrl($"{baseUri}/Searchdomain", "Queries", parameters));
        }

        public async Task<EntityQueryResults> SearchdomainQueryAsync(string query)
        {
            return await SearchdomainQueryAsync(searchdomain, query);
        }

        public async Task<EntityQueryResults> SearchdomainQueryAsync(string searchdomain, string query, int? topN = null, bool returnAttributes = false)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain},
                {"query", query}
            };
            if (topN is not null) parameters.Add("topN", ((int)topN).ToString());
            if (returnAttributes) parameters.Add("returnAttributes", returnAttributes.ToString());

            return await FetchUrlAndProcessJson<EntityQueryResults>(HttpMethod.Post, GetUrl($"{baseUri}/Searchdomain", "Query", parameters), null);
        }

        public async Task<SearchdomainDeleteSearchResult> SearchdomainDeleteQueryAsync(string searchdomain, string query)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain},
                {"query", query}
            };
            return await FetchUrlAndProcessJson<SearchdomainDeleteSearchResult>(HttpMethod.Delete, GetUrl($"{baseUri}/Searchdomain", "Query", parameters));
        }

        public async Task<SearchdomainUpdateSearchResult> SearchdomainUpdateQueryAsync(string searchdomain, string query, List<ResultItem> results)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain},
                {"query", query}
            };
            return await FetchUrlAndProcessJson<SearchdomainUpdateSearchResult>(
                HttpMethod.Patch,
                GetUrl($"{baseUri}/Searchdomain", "Query", parameters),
                new StringContent(JsonSerializer.Serialize(results), Encoding.UTF8, "application/json"));
        }

        public async Task<SearchdomainSettingsResults> SearchdomainGetSettingsAsync(string searchdomain)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain}
            };
            return await FetchUrlAndProcessJson<SearchdomainSettingsResults>(HttpMethod.Get, GetUrl($"{baseUri}/Searchdomain", "Settings", parameters));
        }

        public async Task<SearchdomainUpdateResults> SearchdomainUpdateSettingsAsync(string searchdomain, SearchdomainSettings searchdomainSettings)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain}
            };
            StringContent content = new(JsonSerializer.Serialize(searchdomainSettings), Encoding.UTF8, "application/json");
            return await FetchUrlAndProcessJson<SearchdomainUpdateResults>(HttpMethod.Put, GetUrl($"{baseUri}/Searchdomain", "Settings", parameters), content);
        }

        public async Task<SearchdomainSearchCacheSizeResults> SearchdomainGetQueryCacheSizeAsync(string searchdomain)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain}
            };
            return await FetchUrlAndProcessJson<SearchdomainSearchCacheSizeResults>(HttpMethod.Get, GetUrl($"{baseUri}/Searchdomain/QueryCache", "Size", parameters));
        }

        public async Task<SearchdomainInvalidateCacheResults> SearchdomainClearQueryCache(string searchdomain)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain}
            };
            return await FetchUrlAndProcessJson<SearchdomainInvalidateCacheResults>(HttpMethod.Post, GetUrl($"{baseUri}/Searchdomain/QueryCache", "Clear", parameters), null);
        }

        public async Task<SearchdomainGetDatabaseSizeResult> SearchdomainGetDatabaseSizeAsync(string searchdomain)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain}
            };
            return await FetchUrlAndProcessJson<SearchdomainGetDatabaseSizeResult>(HttpMethod.Get, GetUrl($"{baseUri}/Searchdomain/Database", "Size", parameters));
        }

        public async Task<ServerGetModelsResult> ServerGetModelsAsync()
        {
            return await FetchUrlAndProcessJson<ServerGetModelsResult>(HttpMethod.Get, GetUrl($"{baseUri}/Server", "Models", []));
        }

        public async Task<ServerGetEmbeddingCacheSizeResult> ServerGetEmbeddingCacheSizeAsync()
        {
            return await FetchUrlAndProcessJson<ServerGetEmbeddingCacheSizeResult>(HttpMethod.Get, GetUrl($"{baseUri}/Server/EmbeddingCache", "Size", []));
        }

        private async Task<T> FetchUrlAndProcessJson<T>(HttpMethod httpMethod, string url, HttpContent? content = null)
        {
            HttpRequestMessage requestMessage = new(httpMethod, url)
            {
                Content = content,
            };
            requestMessage.Headers.Add("X-API-KEY", apiKey);
            using var client = new HttpClient();
            var response = await client.SendAsync(requestMessage);
            string responseContent = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException(responseContent); // TODO implement distinct exceptions
            if (response.StatusCode == HttpStatusCode.InternalServerError) throw new Exception($"Request was unsuccessful due to an internal server error: {responseContent}"); // TODO implement proper InternalServerErrorException
            var result = JsonSerializer.Deserialize<T>(responseContent)
                ?? throw new Exception($"Failed to deserialize JSON to type {typeof(T).Name}");
            return result;
        }

        public static string GetUrl(string baseUri, string endpoint, Dictionary<string, string> parameters)
        {
            var uriBuilder = new UriBuilder($"{baseUri}/{endpoint}");
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            foreach (var param in parameters)
            {
                query[param.Key] = param.Value;
            }
            uriBuilder.Query = query.ToString() ?? "";
            return uriBuilder.Uri.ToString();
        }
}

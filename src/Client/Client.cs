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
            var url = $"{baseUri}/Entities?apiKey={HttpUtility.UrlEncode(apiKey)}&searchdomain={HttpUtility.UrlEncode(searchdomain)}&returnEmbeddings={HttpUtility.UrlEncode(returnEmbeddings.ToString())}";
            return await GetUrlAndProcessJson<EntityListResults>(url);
        }

        public async Task<EntityIndexResult> EntityIndexAsync(List<JSONEntity> jsonEntity)
        {
            return await EntityIndexAsync(JsonSerializer.Serialize(jsonEntity));
        }

        public async Task<EntityIndexResult> EntityIndexAsync(string jsonEntity)
        {
            var content = new StringContent(jsonEntity, Encoding.UTF8, "application/json");
            return await PutUrlAndProcessJson<EntityIndexResult>(GetUrl($"{baseUri}", "Entities", apiKey, []), content);
        }

        public async Task<EntityDeleteResults> EntityDeleteAsync(string entityName)
        {
            return await EntityDeleteAsync(searchdomain, entityName);
        }

        public async Task<EntityDeleteResults> EntityDeleteAsync(string searchdomain, string entityName)
        {
            var url = $"{baseUri}/Entity?apiKey={HttpUtility.UrlEncode(apiKey)}&searchdomain={HttpUtility.UrlEncode(searchdomain)}&entity={HttpUtility.UrlEncode(entityName)}";
            return await DeleteUrlAndProcessJson<EntityDeleteResults>(url);
        }

        public async Task<SearchdomainListResults> SearchdomainListAsync()
        {
            return await GetUrlAndProcessJson<SearchdomainListResults>(GetUrl($"{baseUri}", "Searchdomains", apiKey, []));
        }

        public async Task<SearchdomainCreateResults> SearchdomainCreateAsync()
        {
            return await SearchdomainCreateAsync(searchdomain);
        }

        public async Task<SearchdomainCreateResults> SearchdomainCreateAsync(string searchdomain, SearchdomainSettings searchdomainSettings = new())
        {
            return await PostUrlAndProcessJson<SearchdomainCreateResults>(GetUrl($"{baseUri}", "Searchdomain", apiKey, new Dictionary<string, string>()
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
            return await DeleteUrlAndProcessJson<SearchdomainDeleteResults>(GetUrl($"{baseUri}", "Searchdomain", apiKey, new Dictionary<string, string>()
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
            return await PutUrlAndProcessJson<SearchdomainUpdateResults>(GetUrl($"{baseUri}", "Searchdomain", apiKey, new Dictionary<string, string>()
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
            return await GetUrlAndProcessJson<SearchdomainSearchesResults>(GetUrl($"{baseUri}/Searchdomain", "Queries", apiKey, parameters));
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

            return await PostUrlAndProcessJson<EntityQueryResults>(GetUrl($"{baseUri}/Searchdomain", "Query", apiKey, parameters), null);
        }

        public async Task<SearchdomainDeleteSearchResult> SearchdomainDeleteQueryAsync(string searchdomain, string query)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain},
                {"query", query}
            };
            return await DeleteUrlAndProcessJson<SearchdomainDeleteSearchResult>(GetUrl($"{baseUri}/Searchdomain", "Query", apiKey, parameters));
        }

        public async Task<SearchdomainUpdateSearchResult> SearchdomainUpdateQueryAsync(string searchdomain, string query, List<ResultItem> results)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain},
                {"query", query}
            };
            return await PatchUrlAndProcessJson<SearchdomainUpdateSearchResult>(
                GetUrl($"{baseUri}/Searchdomain", "Query", apiKey, parameters),
                new StringContent(JsonSerializer.Serialize(results), Encoding.UTF8, "application/json"));
        }

        public async Task<SearchdomainSettingsResults> SearchdomainGetSettingsAsync(string searchdomain)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain}
            };
            return await GetUrlAndProcessJson<SearchdomainSettingsResults>(GetUrl($"{baseUri}/Searchdomain", "Settings", apiKey, parameters));
        }

        public async Task<SearchdomainUpdateResults> SearchdomainUpdateSettingsAsync(string searchdomain, SearchdomainSettings searchdomainSettings)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain}
            };
            StringContent content = new(JsonSerializer.Serialize(searchdomainSettings), Encoding.UTF8, "application/json");
            return await PutUrlAndProcessJson<SearchdomainUpdateResults>(GetUrl($"{baseUri}/Searchdomain", "Settings", apiKey, parameters), content);
        }

        public async Task<SearchdomainSearchCacheSizeResults> SearchdomainGetQueryCacheSizeAsync(string searchdomain)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain}
            };
            return await GetUrlAndProcessJson<SearchdomainSearchCacheSizeResults>(GetUrl($"{baseUri}/Searchdomain/QueryCache", "Size", apiKey, parameters));
        }

        public async Task<SearchdomainInvalidateCacheResults> SearchdomainClearQueryCache(string searchdomain)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain}
            };
            return await PostUrlAndProcessJson<SearchdomainInvalidateCacheResults>(GetUrl($"{baseUri}/Searchdomain/QueryCache", "Clear", apiKey, parameters), null);
        }

        public async Task<SearchdomainGetDatabaseSizeResult> SearchdomainGetDatabaseSizeAsync(string searchdomain)
        {
            Dictionary<string, string> parameters = new()
            {
                {"searchdomain", searchdomain}
            };
            return await GetUrlAndProcessJson<SearchdomainGetDatabaseSizeResult>(GetUrl($"{baseUri}/Searchdomain/Database", "Size", apiKey, parameters));
        }

        public async Task<ServerGetModelsResult> ServerGetModelsAsync()
        {
            return await GetUrlAndProcessJson<ServerGetModelsResult>(GetUrl($"{baseUri}/Server", "Models", apiKey, []));
        }

        private static async Task<T> GetUrlAndProcessJson<T>(string url)
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(url);
            string responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<T>(responseContent)
                ?? throw new Exception($"Failed to deserialize JSON to type {typeof(T).Name}");
            return result;
        }

        private static async Task<T> PostUrlAndProcessJson<T>(string url, HttpContent? content)
        {
            using var client = new HttpClient();
            var response = await client.PostAsync(url, content);
            string responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<T>(responseContent)
                ?? throw new Exception($"Failed to deserialize JSON to type {typeof(T).Name}");
            return result;
        }

        private static async Task<T> PutUrlAndProcessJson<T>(string url, HttpContent content)
        {
            using var client = new HttpClient();
            var response = await client.PutAsync(url, content);
            string responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<T>(responseContent)
                ?? throw new Exception($"Failed to deserialize JSON to type {typeof(T).Name}");
            return result;
        }

        private static async Task<T> PatchUrlAndProcessJson<T>(string url, HttpContent content)
        {
            using var client = new HttpClient();
            var response = await client.PatchAsync(url, content);
            string responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<T>(responseContent)
                ?? throw new Exception($"Failed to deserialize JSON to type {typeof(T).Name}");
            return result;
        }

        private static async Task<T> DeleteUrlAndProcessJson<T>(string url)
        {
            using var client = new HttpClient();
            var response = await client.DeleteAsync(url);
            string responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<T>(responseContent)
                ?? throw new Exception($"Failed to deserialize JSON to type {typeof(T).Name}");
            return result;
        }

        public static string GetUrl(string baseUri, string endpoint, string apiKey, Dictionary<string, string> parameters)
        {
            var uriBuilder = new UriBuilder($"{baseUri}/{endpoint}");
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            if (apiKey.Length > 0) query["apiKey"] = apiKey;
            foreach (var param in parameters)
            {
                query[param.Key] = param.Value;
            }
            uriBuilder.Query = query.ToString() ?? "";
            return uriBuilder.Uri.ToString();
        }
}

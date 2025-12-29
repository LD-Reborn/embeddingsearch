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

        public async Task<SearchdomainListResults> SearchdomainListAsync()
        {
            return await GetUrlAndProcessJson<SearchdomainListResults>(GetUrl($"{baseUri}", "Searchdomains", apiKey, []));
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

        public async Task<EntityQueryResults> EntityQueryAsync(string query)
        {
            return await EntityQueryAsync(searchdomain, query);
        }

        public async Task<EntityQueryResults> EntityQueryAsync(string searchdomain, string query)
        {
            return await PostUrlAndProcessJson<EntityQueryResults>(GetUrl($"{baseUri}/Searchdomain", "Query", apiKey, new Dictionary<string, string>()
            {
                {"searchdomain", searchdomain},
                {"query", query}
            }), null);
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

        public async Task<EntityListResults> EntityListAsync(bool returnEmbeddings = false)
        {
            return await EntityListAsync(searchdomain, returnEmbeddings);
        }

        public async Task<EntityListResults> EntityListAsync(string searchdomain, bool returnEmbeddings = false)
        {
            var url = $"{baseUri}/Entities?apiKey={HttpUtility.UrlEncode(apiKey)}&searchdomain={HttpUtility.UrlEncode(searchdomain)}&returnEmbeddings={HttpUtility.UrlEncode(returnEmbeddings.ToString())}";
            return await GetUrlAndProcessJson<EntityListResults>(url);
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

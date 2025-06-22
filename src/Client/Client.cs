using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Reflection.Metadata.Ecma335;
using Server.Models;

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
            var url = $"{baseUri}/Searchdomain/List?apiKey={HttpUtility.UrlEncode(apiKey)}";
            return await GetUrlAndProcessJson<SearchdomainListResults>(url);
        }

        public async Task<SearchdomainDeleteResults> SearchdomainDeleteAsync()
        {
            return await SearchdomainDeleteAsync(searchdomain);
        }

        public async Task<SearchdomainDeleteResults> SearchdomainDeleteAsync(string searchdomain)
        {
            var url = $"{baseUri}/Searchdomain/Delete?apiKey={HttpUtility.UrlEncode(apiKey)}&searchdomain={HttpUtility.UrlEncode(searchdomain)}";
            return await GetUrlAndProcessJson<SearchdomainDeleteResults>(url);
        }

        public async Task<SearchdomainCreateResults> SearchdomainCreateAsync()
        {
            return await SearchdomainCreateAsync(searchdomain);
        }

        public async Task<SearchdomainCreateResults> SearchdomainCreateAsync(string searchdomain)
        {
            var url = $"{baseUri}/Searchdomain/Create?apiKey={HttpUtility.UrlEncode(apiKey)}&searchdomain={HttpUtility.UrlEncode(searchdomain)}";
            return await GetUrlAndProcessJson<SearchdomainCreateResults>(url);
        }

        public async Task<SearchdomainUpdateResults> SearchdomainUpdateAsync(string newName, string settings = "{}")
        {
            SearchdomainUpdateResults updateResults = await SearchdomainUpdateAsync(searchdomain, newName, settings);
            searchdomain = newName;
            return updateResults;
        }

        public async Task<SearchdomainUpdateResults> SearchdomainUpdateAsync(string searchdomain, string newName, string settings = "{}")
        {
            var url = $"{baseUri}/Searchdomain/Update?apiKey={HttpUtility.UrlEncode(apiKey)}&searchdomain={HttpUtility.UrlEncode(searchdomain)}&newName={HttpUtility.UrlEncode(newName)}&settings={HttpUtility.UrlEncode(settings)}";
            return await GetUrlAndProcessJson<SearchdomainUpdateResults>(url);
        }

        public async Task<EntityQueryResults> EntityQueryAsync(string query)
        {
            return await EntityQueryAsync(searchdomain, query);
        }

        public async Task<EntityQueryResults> EntityQueryAsync(string searchdomain, string query)
        {
            var url = $"{baseUri}/Entity/Query?apiKey={HttpUtility.UrlEncode(apiKey)}&searchdomain={HttpUtility.UrlEncode(searchdomain)}&query={HttpUtility.UrlEncode(query)}";
            return await GetUrlAndProcessJson<EntityQueryResults>(url);
        }

        public async Task<EntityIndexResult> EntityIndexAsync(List<Server.JSONEntity> jsonEntity)
        {
            return await EntityIndexAsync(JsonSerializer.Serialize(jsonEntity));
        }

        public async Task<EntityIndexResult> EntityIndexAsync(string jsonEntity)
        {
            var url = $"{baseUri}/Entity/Index?apiKey={HttpUtility.UrlEncode(apiKey)}";
            var content = new StringContent(jsonEntity, Encoding.UTF8, "application/json");
            return await PostUrlAndProcessJson<EntityIndexResult>(url, content);//new FormUrlEncodedContent(values));
        }

        public async Task<EntityListResults> EntityListAsync(bool returnEmbeddings = false)
        {
            return await EntityListAsync(searchdomain, returnEmbeddings);
        }

        public async Task<EntityListResults> EntityListAsync(string searchdomain, bool returnEmbeddings = false)
        {
            var url = $"{baseUri}/Entity/List?apiKey={HttpUtility.UrlEncode(apiKey)}&searchdomain={HttpUtility.UrlEncode(searchdomain)}&returnEmbeddings={HttpUtility.UrlEncode(returnEmbeddings.ToString())}";
            return await GetUrlAndProcessJson<EntityListResults>(url);
        }

        public async Task<EntityDeleteResults> EntityDeleteAsync(string entityName)
        {
            return await EntityDeleteAsync(searchdomain, entityName);
        }

        public async Task<EntityDeleteResults> EntityDeleteAsync(string searchdomain, string entityName)
        {
            var url = $"{baseUri}/Entity/Delete?apiKey={HttpUtility.UrlEncode(apiKey)}&searchdomain={HttpUtility.UrlEncode(searchdomain)}&entity={HttpUtility.UrlEncode(entityName)}";
            return await GetUrlAndProcessJson<EntityDeleteResults>(url);
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
        private static async Task<T> PostUrlAndProcessJson<T>(string url, HttpContent content)
        {
            using var client = new HttpClient();
            var response = await client.PostAsync(url, content);
            string responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine("DEBUG@GetUrlAndProcessJson");
            Console.WriteLine(responseContent);
            var result = JsonSerializer.Deserialize<T>(responseContent)
                ?? throw new Exception($"Failed to deserialize JSON to type {typeof(T).Name}");
            return result;
        }
}

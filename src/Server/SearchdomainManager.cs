using MySql.Data.MySqlClient;
using System.Data.Common;
using OllamaSharp;
using Microsoft.IdentityModel.Tokens;
using Server.Exceptions;
using Server.Migrations;

namespace Server;

public class SearchdomainManager
{
    private Dictionary<string, Searchdomain> searchdomains = [];
    private readonly ILogger<SearchdomainManager> _logger;
    private readonly IConfiguration _config;
    private readonly string ollamaURL;
    private readonly string connectionString;
    public OllamaApiClient client;
    private MySqlConnection connection;
    public SQLHelper helper;
    public Dictionary<string, Dictionary<string, float[]>> embeddingCache;

    public SearchdomainManager(ILogger<SearchdomainManager> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        embeddingCache = [];
        ollamaURL = _config.GetSection("Embeddingsearch")["OllamaURL"] ?? "";
        connectionString = _config.GetSection("Embeddingsearch").GetConnectionString("SQL") ?? "";
        if (ollamaURL.IsNullOrEmpty() || connectionString.IsNullOrEmpty())
        {
            throw new ServerConfigurationException("Ollama URL or connection string is empty");
        }
        client = new(new Uri(ollamaURL));
        connection = new MySqlConnection(connectionString);
        connection.Open();
        helper = new SQLHelper(connection, connectionString);
        try
        {
            DatabaseMigrations.Migrate(helper);
        }
        catch (Exception ex)
        {
            _logger.LogCritical("Unable to migrate the database due to the exception: {ex}", [ex.Message]);
            throw;
        }
    }

    public Searchdomain GetSearchdomain(string searchdomain)
    {
        if (searchdomains.TryGetValue(searchdomain, out Searchdomain? value))
        {
            return value;
        }
        try
        {
            return SetSearchdomain(searchdomain, new Searchdomain(searchdomain, connectionString, client, embeddingCache, _logger));
        }
        catch (MySqlException)
        {
            _logger.LogError("Unable to find the searchdomain {searchdomain}", searchdomain);
            throw new Exception($"Unable to find the searchdomain {searchdomain}");
        }
    }

    public void InvalidateSearchdomainCache(string searchdomainName)
    {
        GetSearchdomain(searchdomainName).UpdateEntityCache();
    }

    public List<string> ListSearchdomains()
    {
        lock (helper.connection)
        {
            DbDataReader reader = helper.ExecuteSQLCommand("SELECT name FROM searchdomain", []);
            List<string> results = [];
            while (reader.Read())
            {
                results.Add(reader.GetString(0));
            }
            reader.Close();
            return results;
        }
    }

    public int CreateSearchdomain(string searchdomain, string settings = "{}")
    {
        if (searchdomains.TryGetValue(searchdomain, out Searchdomain? value))
        {
            _logger.LogError("Searchdomain {searchdomain} could not be created, as it already exists", [searchdomain]);
            throw new Exception("Searchdomain already exists"); // TODO create proper SearchdomainAlreadyExists exception
        }
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", searchdomain },
            { "settings", settings}
        };
        return helper.ExecuteSQLCommandGetInsertedID("INSERT INTO searchdomain (name, settings) VALUES (@name, @settings)", parameters);
    }

    public int DeleteSearchdomain(string searchdomain)
    {
        Searchdomain searchdomain_ = GetSearchdomain(searchdomain);
        int counter = 0;
        while (searchdomain_.entityCache.Count > 0)
        {
            DatabaseHelper.RemoveEntity(searchdomain_.entityCache, helper, searchdomain_.entityCache.First().name, searchdomain);
            counter += 1;
        }
        _logger.LogDebug($"Number of entities deleted as part of deleting the searchdomain \"{searchdomain}\": {counter}");
        helper.ExecuteSQLNonQuery("DELETE FROM searchdomain WHERE name = @name", new() {{"name", searchdomain}});
        searchdomains.Remove(searchdomain);
        _logger.LogDebug($"Searchdomain has been successfully removed");
        return counter;
    }
    private Searchdomain SetSearchdomain(string name, Searchdomain searchdomain)
    {
        searchdomains[name] = searchdomain;
        return searchdomain;
    }
}

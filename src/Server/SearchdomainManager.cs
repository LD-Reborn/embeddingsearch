using MySql.Data.MySqlClient;
using System.Data.Common;
using OllamaSharp;
using Microsoft.IdentityModel.Tokens;
using Server.Exceptions;

namespace Server;

public class SearchdomainManager
{
    private Dictionary<string, Searchdomain> searchdomains = [];
    private readonly ILogger<SearchdomainManager> _logger;
    private readonly IConfiguration _config;
    private readonly string ollamaURL;
    private readonly string connectionString;
    private OllamaApiClient client;
    private MySqlConnection connection;

    public SearchdomainManager(ILogger<SearchdomainManager> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        ollamaURL = _config.GetSection("Embeddingsearch")["OllamaURL"] ?? "";
        connectionString = _config.GetSection("Embeddingsearch").GetConnectionString("SQL") ?? "";
        if (ollamaURL.IsNullOrEmpty() || connectionString.IsNullOrEmpty())
        {
            throw new ServerConfigurationException("Ollama URL or connection string is empty");
        }
        client = new(new Uri(ollamaURL));
        connection = new MySqlConnection(connectionString);
        connection.Open();
    }

    public Searchdomain GetSearchdomain(string searchdomain)
    {
        if (searchdomains.TryGetValue(searchdomain, out Searchdomain? value))
        {
            return value;
        }
        try
        {
            return SetSearchdomain(searchdomain, new Searchdomain(searchdomain, connectionString, client));
        } catch (MySqlException)
        {
            _logger.LogError("Unable to find the searchdomain {searchdomain}", searchdomain);
            throw new Exception($"Unable to find the searchdomain {searchdomain}");
        }
    }

    public void InvalidateSearchdomainCache(string searchdomain)
    {
        searchdomains.Remove(searchdomain);
    }

    public List<string> ListSearchdomains()
    {
        DbDataReader reader = ExecuteSQLCommand("SELECT name FROM searchdomain", []);
        List<string> results = [];
        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }
        reader.Close();
        return results;
    }

    public int CreateSearchdomain(string searchdomain, string settings = "{}")
    {
        if (searchdomains.TryGetValue(searchdomain, out Searchdomain? value))
        {
            throw new Exception("Searchdomain already exists"); // TODO create proper SearchdomainAlreadyExists exception
        }
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", searchdomain },
            { "settings", settings}
        };
        return ExecuteSQLCommandGetInsertedID("INSERT INTO searchdomain (name, settings) VALUES (@name, @settings)", parameters);
    }

    public int DeleteSearchdomain(string searchdomain)
    {
        Searchdomain searchdomain_ = GetSearchdomain(searchdomain);
        int counter = 0;
        while (searchdomain_.entityCache.Count > 0)
        {
            searchdomain_.RemoveEntity(searchdomain_.entityCache.First().name);
            counter += 1;
        }
        _logger.LogDebug($"Number of entities deleted as part of deleting the searchdomain \"{searchdomain}\": {counter}");
        searchdomain_.ExecuteSQLNonQuery("DELETE FROM searchdomain WHERE name = @name", new() {{"name", searchdomain}});
        searchdomains.Remove(searchdomain);
        _logger.LogDebug($"Searchdomain has been successfully removed");
        return counter;
    }

    public DbDataReader ExecuteSQLCommand(string query, Dictionary<string, dynamic> parameters)
    {
        using MySqlCommand command = connection.CreateCommand();
        command.CommandText = query;
        foreach (KeyValuePair<string, dynamic> parameter in parameters)
        {
            command.Parameters.AddWithValue($"@{parameter.Key}", parameter.Value);
        }
        return command.ExecuteReader();
    }

    public int ExecuteSQLCommandGetInsertedID(string query, Dictionary<string, dynamic> parameters)
    {
        using MySqlCommand command = connection.CreateCommand();

        command.CommandText = query;
        foreach (KeyValuePair<string, dynamic> parameter in parameters)
        {
            command.Parameters.AddWithValue($"@{parameter.Key}", parameter.Value);
        }
        command.ExecuteNonQuery();
        command.CommandText = "SELECT LAST_INSERT_ID();";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private Searchdomain SetSearchdomain(string name, Searchdomain searchdomain)
    {
        searchdomains[name] = searchdomain;
        return searchdomain;
    }


}

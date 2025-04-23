using embeddingsearch;
using MySql.Data.MySqlClient;
using System.Data.Common;
using OllamaSharp;

namespace server;

public class SearchomainManager
{
    private Dictionary<string, Searchdomain> searchdomains = [];
    private readonly ILogger<SearchomainManager> _logger;
    private readonly IConfiguration _config;
    private readonly string ollamaURL;
    private readonly string connectionString;
    private OllamaApiClient client;
    private MySqlConnection connection;

    public SearchomainManager(ILogger<SearchomainManager> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        ollamaURL = _config.GetSection("Embeddingsearch")["OllamaURL"] ?? "";
        connectionString = _config.GetSection("Embeddingsearch").GetConnectionString("SQL") ?? "";
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
        foreach (Entity entity in searchdomain_.entityCache)
        {
            searchdomain_.DatabaseRemoveEntity(entity.name);
            counter += 1;
        }
        _logger.LogDebug($"Number of entities deleted as part of deleting the searchdomain \"{searchdomain}\": {counter}");
        searchdomain_.ExecuteSQLNonQuery("DELETE FROM entity WHERE id_searchdomain = @id", new() {{"id", searchdomain_.id}}); // Cleanup // TODO add rows affected
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

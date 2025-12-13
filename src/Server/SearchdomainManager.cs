using MySql.Data.MySqlClient;
using System.Data.Common;
using Server.Migrations;
using Server.Helper;
using Server.Exceptions;

namespace Server;

public class SearchdomainManager
{
    private Dictionary<string, Searchdomain> searchdomains = [];
    private readonly ILogger<SearchdomainManager> _logger;
    private readonly IConfiguration _config;
    public readonly AIProvider aIProvider;
    private readonly DatabaseHelper _databaseHelper;
    private readonly string connectionString;
    private MySqlConnection connection;
    public SQLHelper helper;
    public Dictionary<string, Dictionary<string, float[]>> embeddingCache;

    public SearchdomainManager(ILogger<SearchdomainManager> logger, IConfiguration config, AIProvider aIProvider, DatabaseHelper databaseHelper)
    {
        _logger = logger;
        _config = config;
        this.aIProvider = aIProvider;
        _databaseHelper = databaseHelper;
        embeddingCache = [];
        connectionString = _config.GetSection("Embeddingsearch").GetConnectionString("SQL") ?? "";
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
            return SetSearchdomain(searchdomain, new Searchdomain(searchdomain, connectionString, aIProvider, embeddingCache, _logger));
        }
        catch (MySqlException)
        {
            _logger.LogError("Unable to find the searchdomain {searchdomain}", searchdomain);
            throw new SearchdomainNotFoundException(searchdomain);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unable to load the searchdomain {searchdomain} due to the following exception: {ex}", [searchdomain, ex.Message]);
            throw;
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
            throw new SearchdomainAlreadyExistsException(searchdomain);
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
        int counter = _databaseHelper.RemoveAllEntities(helper, searchdomain);
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

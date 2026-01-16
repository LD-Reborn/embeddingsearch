using MySql.Data.MySqlClient;
using System.Data.Common;
using Server.Migrations;
using Server.Helper;
using Server.Exceptions;
using AdaptiveExpressions;
using Shared.Models;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Server.Models;
using Shared;

namespace Server;

public class SearchdomainManager
{
    private Dictionary<string, Searchdomain> searchdomains = [];
    private readonly ILogger<SearchdomainManager> _logger;
    private readonly EmbeddingSearchOptions _options;
    public readonly AIProvider aIProvider;
    private readonly DatabaseHelper _databaseHelper;
    private readonly string connectionString;
    private MySqlConnection connection;
    public SQLHelper helper;
    public EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache;
    public long EmbeddingCacheMaxCount;

    public SearchdomainManager(ILogger<SearchdomainManager> logger, IOptions<EmbeddingSearchOptions> options, AIProvider aIProvider, DatabaseHelper databaseHelper)
    {
        _logger = logger;
        _options = options.Value;
        this.aIProvider = aIProvider;
        _databaseHelper = databaseHelper;
        EmbeddingCacheMaxCount = _options.EmbeddingCacheMaxCount;
        embeddingCache = new((int)EmbeddingCacheMaxCount);
        connectionString = _options.ConnectionStrings.SQL;
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
        var searchdomain = GetSearchdomain(searchdomainName);
        searchdomain.UpdateEntityCache();
        searchdomain.InvalidateSearchCache();
    }

    public List<string> ListSearchdomains()
    {
        lock (helper.connection)
        {
            DbDataReader reader = helper.ExecuteSQLCommand("SELECT name FROM searchdomain", []);
            List<string> results = [];
            try
            {
                while (reader.Read())
                {
                    results.Add(reader.GetString(0));
                }
                return results;                
            }
            finally
            {
                reader.Close();
            }
        }
    }

    public int CreateSearchdomain(string searchdomain, SearchdomainSettings settings)
    {
        return CreateSearchdomain(searchdomain, JsonSerializer.Serialize(settings));
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

    public bool IsSearchdomainLoaded(string name)
    {
        return searchdomains.ContainsKey(name);
    }
}

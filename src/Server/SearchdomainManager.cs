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
using System.Diagnostics;

namespace Server;

public class SearchdomainManager : IDisposable
{
    private Dictionary<string, Searchdomain> _searchdomains = [];
    private readonly ILogger<SearchdomainManager> _logger;
    private readonly EmbeddingSearchOptions _options;
    public readonly AIProvider AiProvider;
    private readonly DatabaseHelper _databaseHelper;
    private readonly string connectionString;
    private MySqlConnection _connection;
    public SQLHelper Helper;
    public EnumerableLruCache<string, Dictionary<string, float[]>> EmbeddingCache;
    public long EmbeddingCacheMaxCount;
    private bool _disposed = false;

    public SearchdomainManager(ILogger<SearchdomainManager> logger, IOptions<EmbeddingSearchOptions> options, AIProvider aIProvider, DatabaseHelper databaseHelper)
    {
        _logger = logger;
        _options = options.Value;
        this.AiProvider = aIProvider;
        _databaseHelper = databaseHelper;
        EmbeddingCacheMaxCount = _options.Cache.CacheTopN;
        if (options.Value.Cache.StoreEmbeddingCache)
        {
            var stopwatch = Stopwatch.StartNew();
            EmbeddingCache = CacheHelper.GetEmbeddingStore(options.Value);
            stopwatch.Stop();
            _logger.LogInformation("GetEmbeddingStore completed in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);
        } else
        {
            EmbeddingCache = new((int)EmbeddingCacheMaxCount);
        }
        connectionString = _options.ConnectionStrings.SQL;
        _connection = new MySqlConnection(connectionString);
        _connection.Open();
        Helper = new SQLHelper(_connection, connectionString);
    }

    public Searchdomain GetSearchdomain(string searchdomain)
    {
        if (_searchdomains.TryGetValue(searchdomain, out Searchdomain? value))
        {
            return value;
        }
        try
        {
            return SetSearchdomain(searchdomain, new Searchdomain(searchdomain, connectionString, Helper, AiProvider, EmbeddingCache, _logger));
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

    public async Task<List<string>> ListSearchdomainsAsync()
    {
        return await Helper.ExecuteQueryAsync("SELECT name FROM searchdomain", [], x => x.GetString(0));
    }

    public async Task<int> CreateSearchdomain(string searchdomain, SearchdomainSettings settings)
    {
        return await CreateSearchdomain(searchdomain, JsonSerializer.Serialize(settings));
    }

    public async Task<int> CreateSearchdomain(string searchdomain, string settings = "{}")
    {
        if (_searchdomains.TryGetValue(searchdomain, out Searchdomain? value))
        {
            _logger.LogError("Searchdomain {searchdomain} could not be created, as it already exists", [searchdomain]);
            throw new SearchdomainAlreadyExistsException(searchdomain);
        }
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", searchdomain },
            { "settings", settings}
        };
        int id = await Helper.ExecuteSQLCommandGetInsertedID("INSERT INTO searchdomain (name, settings) VALUES (@name, @settings)", parameters);
        _searchdomains.Add(searchdomain, new(searchdomain, connectionString, Helper, AiProvider, EmbeddingCache, _logger));
        return id;
    }

    public async Task<int> DeleteSearchdomain(string searchdomain)
    {
        int counter = await _databaseHelper.RemoveAllEntities(Helper, searchdomain);
        _logger.LogDebug($"Number of entities deleted as part of deleting the searchdomain \"{searchdomain}\": {counter}");
        await Helper.ExecuteSQLNonQuery("DELETE FROM searchdomain WHERE name = @name", new() {{"name", searchdomain}});
        _searchdomains.Remove(searchdomain);
        _logger.LogDebug($"Searchdomain has been successfully removed");
        return counter;
    }

    private Searchdomain SetSearchdomain(string name, Searchdomain searchdomain)
    {
        _searchdomains[name] = searchdomain;
        return searchdomain;
    }

    public bool IsSearchdomainLoaded(string name)
    {
        return _searchdomains.ContainsKey(name);
    }

    // Cleanup procedure
    private async Task Cleanup()
    {
        try
        {
            if (_options.Cache.StoreEmbeddingCache)
            {
                var stopwatch = Stopwatch.StartNew();
                await CacheHelper.UpdateEmbeddingStore(EmbeddingCache, _options);
                stopwatch.Stop();
                _logger.LogInformation("UpdateEmbeddingStore completed in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);
            }
            _logger.LogInformation("SearchdomainManager cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SearchdomainManager cleanup");
        }
    }

    public void Dispose()
    {
        Dispose(true).Wait();
        GC.SuppressFinalize(this);
    }

    protected virtual async Task Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            await Cleanup();
            _disposed = true;
        }
    }
}

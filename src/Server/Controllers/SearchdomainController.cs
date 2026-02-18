using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ElmahCore;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Server.Exceptions;
using Server.Helper;
using Shared;
using Shared.Models;

namespace Server.Controllers;

[ApiController]
[Route("[controller]")]
public class SearchdomainController : ControllerBase
{
    private readonly ILogger<SearchdomainController> _logger;
    private readonly IConfiguration _config;
    private SearchdomainManager _domainManager;

    public SearchdomainController(ILogger<SearchdomainController> logger, IConfiguration config, SearchdomainManager domainManager)
    {
        _logger = logger;
        _config = config;
        _domainManager = domainManager;
    }

    /// <summary>
    /// Lists all searchdomains
    /// </summary>
    [HttpGet("/Searchdomains")]
    public async Task<ActionResult<SearchdomainListResults>> List()
    {
        List<string> results;
        try
        {
            results = await _domainManager.ListSearchdomainsAsync();
        }
        catch (Exception)
        {
            _logger.LogError("Unable to list searchdomains");
            throw;
        }
        SearchdomainListResults searchdomainListResults = new() {Searchdomains = results};
        return Ok(searchdomainListResults);
    }

    /// <summary>
    /// Creates a new searchdomain
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    /// <param name="settings">Optional initial settings</param>
    [HttpPost]
    public async Task<ActionResult<SearchdomainCreateResults>> Create([Required]string searchdomain, [FromBody]SearchdomainSettings settings = new())
    {
        try
        {
            if (settings.QueryCacheSize <= 0)
            {
                settings.QueryCacheSize = 1_000_000; // TODO get rid of this magic number
            }
            int id = await _domainManager.CreateSearchdomain(searchdomain, settings);
            return Ok(new SearchdomainCreateResults(){Id = id, Success = true});
        } catch (Exception)
        {
            _logger.LogError("Unable to create Searchdomain {searchdomain}", [searchdomain]);
            return Ok(new SearchdomainCreateResults() { Id = null, Success = false, Message = $"Unable to create Searchdomain {searchdomain}" });
        }
    }

    /// <summary>
    /// Deletes a searchdomain
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    [HttpDelete]
    public async Task<ActionResult<SearchdomainDeleteResults>> Delete([Required]string searchdomain)
    {
        bool success;
        int deletedEntries;
        string? message = null;
        try
        {
            success = true;
            deletedEntries = await _domainManager.DeleteSearchdomain(searchdomain);
        }
        catch (SearchdomainNotFoundException ex)
        {
            _logger.LogError("Unable to delete searchdomain {searchdomain} - not found", [searchdomain]);
            success = false;
            deletedEntries = 0;
            message = $"Unable to delete searchdomain {searchdomain} - not found";
            ElmahExtensions.RaiseError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unable to delete searchdomain {searchdomain}", [searchdomain]);
            success = false;
            deletedEntries = 0;
            message = ex.Message;
            ElmahExtensions.RaiseError(ex);
        }
        return Ok(new SearchdomainDeleteResults(){Success = success, DeletedEntities = deletedEntries, Message = message});
    }

    /// <summary>
    /// Updates name and settings of a searchdomain
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    /// <param name="newName">Updated name of the searchdomain</param>
    /// <param name="settings">Updated settings of searchdomain</param>
    [HttpPut]
    public ActionResult<SearchdomainUpdateResults> Update([Required]string searchdomain, string newName, [FromBody]SearchdomainSettings? settings)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        if (settings is null)
        {
            Dictionary<string, dynamic> parameters = new()
            {
                {"name", newName},
                {"id", searchdomain_.id}
            };
            searchdomain_.helper.ExecuteSQLNonQuery("UPDATE searchdomain set name = @name WHERE id = @id", parameters);
        } else
        {
            Dictionary<string, dynamic> parameters = new()
            {
                {"name", newName},
                {"settings", settings},
                {"id", searchdomain_.id}
            };
            searchdomain_.helper.ExecuteSQLNonQuery("UPDATE searchdomain set name = @name, settings = @settings WHERE id = @id", parameters);            
        }
        return Ok(new SearchdomainUpdateResults(){Success = true});
    }

    /// <summary>
    /// Gets the query cache of a searchdomain
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    [HttpGet("Queries")]
    public ActionResult<SearchdomainQueriesResults> GetQueries([Required]string searchdomain)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        Dictionary<string, DateTimedSearchResult> searchCache = searchdomain_.queryCache.AsDictionary();
        
        return Ok(new SearchdomainQueriesResults() { Searches = searchCache, Success = true });
    }

    /// <summary>
    /// Executes a query in the searchdomain
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    /// <param name="query">Query to execute</param>
    /// <param name="topN">Return only the top N results</param>
    /// <param name="returnAttributes">Return the attributes of the object</param>
    [HttpPost("Query")]
    public ActionResult<EntityQueryResults> Query([Required]string searchdomain, [Required]string query, int? topN, bool returnAttributes = false)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        List<(float, string)> results = searchdomain_.Search(query, topN);
        List<EntityQueryResult> queryResults = [.. results.Select(r => new EntityQueryResult
        {
            Name = r.Item2,
            Value = r.Item1,
            Attributes = returnAttributes ? (searchdomain_.entityCache[r.Item2]?.attributes ?? null) : null
        })];
        return Ok(new EntityQueryResults(){Results = queryResults, Success = true });
    }

    /// <summary>
    /// Deletes a query from the query cache
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    /// <param name="query">Query to delete</param>
    [HttpDelete("Query")]
    public ActionResult<SearchdomainDeleteSearchResult> DeleteQuery([Required]string searchdomain, [Required]string query)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        EnumerableLruCache<string, DateTimedSearchResult> searchCache = searchdomain_.queryCache;
        bool containsKey = searchCache.ContainsKey(query);
        if (containsKey)
        {
            searchCache.Remove(query);
            return Ok(new SearchdomainDeleteSearchResult() {Success = true});
        }
        return Ok(new SearchdomainDeleteSearchResult() {Success = false, Message = "Query not found in search cache"});
    }

    /// <summary>
    /// Updates a query from the query cache
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    /// <param name="query">Query to update</param>
    /// <param name="results">List of results to apply to the query</param>
    [HttpPatch("Query")]
    public ActionResult<SearchdomainUpdateSearchResult> UpdateQuery([Required]string searchdomain, [Required]string query, [Required][FromBody]List<ResultItem> results)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        EnumerableLruCache<string, DateTimedSearchResult> searchCache = searchdomain_.queryCache;
        bool containsKey = searchCache.ContainsKey(query);
        if (containsKey)
        {
            DateTimedSearchResult element = searchCache[query];
            element.Results = results;
            searchCache[query] = element;
            return Ok(new SearchdomainUpdateSearchResult() {Success = true});
        }
        return Ok(new SearchdomainUpdateSearchResult() {Success = false, Message = "Query not found in search cache"});
    }

    /// <summary>
    /// Get the settings of a searchdomain
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    [HttpGet("Settings")]
    public ActionResult<SearchdomainSettingsResults> GetSettings([Required]string searchdomain)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        SearchdomainSettings settings = searchdomain_.settings;
        return Ok(new SearchdomainSettingsResults() { Settings = settings, Success = true });
    }

    /// <summary>
    /// Update the settings of a searchdomain
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    [HttpPut("Settings")]
    public ActionResult<SearchdomainUpdateResults> UpdateSettings([Required]string searchdomain, [Required][FromBody] SearchdomainSettings request)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        Dictionary<string, dynamic> parameters = new()
        {
            {"settings", JsonSerializer.Serialize(request)},
            {"id", searchdomain_.id}
        };
        searchdomain_.helper.ExecuteSQLNonQuery("UPDATE searchdomain set settings = @settings WHERE id = @id", parameters);
        searchdomain_.settings = request;
        searchdomain_.queryCache.Capacity = request.QueryCacheSize;
        return Ok(new SearchdomainUpdateResults(){Success = true});
    }

    /// <summary>
    /// Get the query cache size of a searchdomain
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    [HttpGet("QueryCache/Size")]
    public ActionResult<SearchdomainQueryCacheSizeResults> GetQueryCacheSize([Required]string searchdomain)
    {
        if (!SearchdomainHelper.IsSearchdomainLoaded(_domainManager, searchdomain))
        {
            return Ok(new SearchdomainQueryCacheSizeResults() { SizeBytes = 0, ElementCount = 0, ElementMaxCount = 0, Success = true });
        }
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        int elementCount = searchdomain_.queryCache.Count;
        int ElementMaxCount = searchdomain_.settings.QueryCacheSize;
        return Ok(new SearchdomainQueryCacheSizeResults() { SizeBytes = searchdomain_.GetSearchCacheSize(), ElementCount = elementCount, ElementMaxCount = ElementMaxCount, Success = true });
    }

    /// <summary>
    /// Clear the query cache of a searchdomain
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    [HttpPost("QueryCache/Clear")]
    public ActionResult<SearchdomainInvalidateCacheResults> InvalidateSearchCache([Required]string searchdomain)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        searchdomain_.InvalidateSearchCache();
        return Ok(new SearchdomainInvalidateCacheResults(){Success = true});
    }

    /// <summary>
    /// Get the disk size of a searchdomain in bytes
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    [HttpGet("Database/Size")]
    public ActionResult<SearchdomainGetDatabaseSizeResult> GetDatabaseSize([Required]string searchdomain)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        long EmbeddingCacheUtilization = DatabaseHelper.GetSearchdomainDatabaseSize(searchdomain_.helper, searchdomain);
        return Ok(new SearchdomainGetDatabaseSizeResult() { SearchdomainDatabaseSizeBytes = EmbeddingCacheUtilization, Success = true });        
    }
}

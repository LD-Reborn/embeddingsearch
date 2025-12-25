using System.Text.Json;
using ElmahCore;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Server.Exceptions;
using Server.Helper;
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

    [HttpGet("List")]
    public ActionResult<SearchdomainListResults> List()
    {
        List<string> results;
        try
        {
            results = _domainManager.ListSearchdomains();
        }
        catch (Exception)
        {
            _logger.LogError("Unable to list searchdomains");
            throw;
        }
        SearchdomainListResults searchdomainListResults = new() {Searchdomains = results};
        return Ok(searchdomainListResults);
    }

    [HttpGet("Create")]
    public ActionResult<SearchdomainCreateResults> Create(string searchdomain, string settings = "{}")
    {
        try
        {
            int id = _domainManager.CreateSearchdomain(searchdomain, settings);
            return Ok(new SearchdomainCreateResults(){Id = id, Success = true});
        } catch (Exception)
        {
            _logger.LogError("Unable to create Searchdomain {searchdomain}", [searchdomain]);
            return Ok(new SearchdomainCreateResults() { Id = null, Success = false, Message = $"Unable to create Searchdomain {searchdomain}" });
        }
    }

    [HttpGet("Delete")]
    public ActionResult<SearchdomainDeleteResults> Delete(string searchdomain)
    {
        bool success;
        int deletedEntries;
        string? message = null;
        try
        {
            success = true;
            deletedEntries = _domainManager.DeleteSearchdomain(searchdomain);
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

    [HttpGet("Update")]
    public ActionResult<SearchdomainUpdateResults> Update(string searchdomain, string newName, string settings = "{}")
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        Dictionary<string, dynamic> parameters = new()
        {
            {"name", newName},
            {"settings", settings},
            {"id", searchdomain_.id}
        };
        searchdomain_.helper.ExecuteSQLNonQuery("UPDATE searchdomain set name = @name, settings = @settings WHERE id = @id", parameters);
        return Ok(new SearchdomainUpdateResults(){Success = true});
    }

    [HttpPost("UpdateSettings")]
    public ActionResult<SearchdomainUpdateResults> UpdateSettings(string searchdomain, [FromBody] SearchdomainSettings request)
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
        return Ok(new SearchdomainUpdateResults(){Success = true});
    }

    [HttpGet("GetSearches")]
    public ActionResult<SearchdomainSearchesResults> GetSearches(string searchdomain)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        Dictionary<string, DateTimedSearchResult> searchCache = searchdomain_.searchCache;
        
        return Ok(new SearchdomainSearchesResults() { Searches = searchCache, Success = true });
    }

    [HttpDelete("Searches")]
    public ActionResult<SearchdomainDeleteSearchResult> DeleteSearch(string searchdomain, string query)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        Dictionary<string, DateTimedSearchResult> searchCache = searchdomain_.searchCache;
        bool containsKey = searchCache.ContainsKey(query);
        if (containsKey)
        {
            searchCache.Remove(query);
            return Ok(new SearchdomainDeleteSearchResult() {Success = true});
        }
        return Ok(new SearchdomainDeleteSearchResult() {Success = false, Message = "Query not found in search cache"});
    }

    [HttpPatch("Searches")]
    public ActionResult<SearchdomainUpdateSearchResult> UpdateSearch(string searchdomain, string query, [FromBody]List<ResultItem> results)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        Dictionary<string, DateTimedSearchResult> searchCache = searchdomain_.searchCache;
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

    [HttpGet("GetSettings")]
    public ActionResult<SearchdomainSettingsResults> GetSettings(string searchdomain)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        SearchdomainSettings settings = searchdomain_.settings;
        return Ok(new SearchdomainSettingsResults() { Settings = settings, Success = true });
    }

    [HttpGet("GetSearchCacheSize")]
    public ActionResult<SearchdomainSearchCacheSizeResults> GetSearchCacheSize(string searchdomain)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        Dictionary<string, DateTimedSearchResult> searchCache = searchdomain_.searchCache;
        long sizeInBytes = 0;
        foreach (var entry in searchCache)
        {
            sizeInBytes += sizeof(int); // string length prefix
            sizeInBytes += entry.Key.Length * sizeof(char); // string characters
            sizeInBytes += entry.Value.EstimateSize();
        }
        return Ok(new SearchdomainSearchCacheSizeResults() { SearchCacheSizeBytes = sizeInBytes, Success = true });
    }

    [HttpGet("ClearSearchCache")]
    public ActionResult<SearchdomainInvalidateCacheResults> InvalidateSearchCache(string searchdomain)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        searchdomain_.InvalidateSearchCache();
        return Ok(new SearchdomainInvalidateCacheResults(){Success = true});
    }

    [HttpGet("GetDatabaseSize")]
    public ActionResult<SearchdomainGetDatabaseSizeResult> GetDatabaseSize(string searchdomain)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        long sizeInBytes = DatabaseHelper.GetSearchdomainDatabaseSize(searchdomain_.helper, searchdomain);
        return Ok(new SearchdomainGetDatabaseSizeResult() { SearchdomainDatabaseSizeBytes = sizeInBytes, Success = true });        
    } 
}

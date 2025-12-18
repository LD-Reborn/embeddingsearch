using System.Text.Json;
using ElmahCore;
using Microsoft.AspNetCore.Mvc;
using Server.Exceptions;
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
        try
        {
            Searchdomain searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
            Dictionary<string, dynamic> parameters = new()
            {
                {"name", newName},
                {"settings", settings},
                {"id", searchdomain_.id}
            };
            searchdomain_.helper.ExecuteSQLNonQuery("UPDATE searchdomain set name = @name, settings = @settings WHERE id = @id", parameters);
        } catch (SearchdomainNotFoundException)
        {
            _logger.LogError("Unable to update searchdomain {searchdomain} - not found", [searchdomain]);
            return Ok(new SearchdomainUpdateResults() { Success = false, Message = $"Unable to update searchdomain {searchdomain} - not found" });
        } catch (Exception ex)
        {
            _logger.LogError("Unable to update searchdomain {searchdomain} - Exception: {ex.Message} - {ex.StackTrace}", [searchdomain, ex.Message, ex.StackTrace]);
            return Ok(new SearchdomainUpdateResults() { Success = false, Message = $"Unable to update searchdomain {searchdomain}" });
        }
        return Ok(new SearchdomainUpdateResults(){Success = true});
    }

    [HttpPost("UpdateSettings")]
    public ActionResult<SearchdomainUpdateResults> UpdateSettings(string searchdomain, [FromBody] SearchdomainSettings request)
    {
        try
        {
            Searchdomain searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
            Dictionary<string, dynamic> parameters = new()
            {
                {"settings", JsonSerializer.Serialize(request)},
                {"id", searchdomain_.id}
            };
            searchdomain_.helper.ExecuteSQLNonQuery("UPDATE searchdomain set settings = @settings WHERE id = @id", parameters);
            searchdomain_.settings = request;
        } catch (SearchdomainNotFoundException)
        {
            _logger.LogError("Unable to update settings for searchdomain {searchdomain} - not found", [searchdomain]);
            return Ok(new SearchdomainUpdateResults() { Success = false, Message = $"Unable to update settings for searchdomain {searchdomain} - not found" });
        } catch (Exception ex)
        {
            _logger.LogError("Unable to update settings for searchdomain {searchdomain} - Exception: {ex.Message} - {ex.StackTrace}", [searchdomain, ex.Message, ex.StackTrace]);
            return Ok(new SearchdomainUpdateResults() { Success = false, Message = $"Unable to update settings for searchdomain {searchdomain}" });
        }
        return Ok(new SearchdomainUpdateResults(){Success = true});
    }

    [HttpGet("GetSearches")]
    public ActionResult<SearchdomainSearchesResults> GetSearches(string searchdomain)
    {
        Searchdomain searchdomain_;
        try
        {
            searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        }
        catch (SearchdomainNotFoundException)
        {
            _logger.LogError("Unable to retrieve the searchdomain {searchdomain} - it likely does not exist yet", [searchdomain]);
            return Ok(new SearchdomainSearchesResults() { Searches = [], Success = false, Message = "Searchdomain not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError("Unable to retrieve the searchdomain {searchdomain} - {ex.Message} - {ex.StackTrace}", [searchdomain, ex.Message, ex.StackTrace]);
            return Ok(new SearchdomainSearchesResults() { Searches = [], Success = false, Message = ex.Message });
        }
        Dictionary<string, DateTimedSearchResult> searchCache = searchdomain_.searchCache;
        
        return Ok(new SearchdomainSearchesResults() { Searches = searchCache, Success = true });
    }

    [HttpGet("GetSettings")]
    public ActionResult<SearchdomainSettingsResults> GetSettings(string searchdomain)
    {
        Searchdomain searchdomain_;
        try
        {
            searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        }
        catch (SearchdomainNotFoundException)
        {
            _logger.LogError("Unable to retrieve the searchdomain {searchdomain} - it likely does not exist yet", [searchdomain]);
            return Ok(new SearchdomainSettingsResults() { Settings = null, Success = false, Message = "Searchdomain not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError("Unable to retrieve the searchdomain {searchdomain} - {ex.Message} - {ex.StackTrace}", [searchdomain, ex.Message, ex.StackTrace]);
            return Ok(new SearchdomainSettingsResults() { Settings = null, Success = false, Message = ex.Message });
        }
        SearchdomainSettings settings = searchdomain_.settings;
        return Ok(new SearchdomainSettingsResults() { Settings = settings, Success = true });
    }
}

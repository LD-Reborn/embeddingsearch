using Microsoft.AspNetCore.Mvc;
using embeddingsearch;
using server.Models;

namespace server.Controllers;

[ApiController]
[Route("[controller]")]
public class SearchdomainController : ControllerBase
{
    private readonly ILogger<SearchdomainController> _logger;
    private readonly IConfiguration _config;
    private SearchomainManager _domainManager;

    public SearchdomainController(ILogger<SearchdomainController> logger, IConfiguration config, SearchomainManager domainManager)
    {
        _logger = logger;
        _config = config;
        _domainManager = domainManager;
    }

    [HttpGet("List")]
    public ActionResult<SearchdomainListResults> List()
    {
        return Ok(_domainManager.ListSearchdomains());
    }

    [HttpGet("Create")]
    public ActionResult<SearchdomainCreateResults> Create(string searchdomain, string settings = "{}")
    {
        return Ok(new SearchdomainCreateResults(){Id = _domainManager.CreateSearchdomain(searchdomain, settings)});
    }

    [HttpGet("Delete")]
    public ActionResult<SearchdomainDeleteResults> Delete(string searchdomain)
    {
        bool success;
        int deletedEntries;
        try
        {
            success = true;
            deletedEntries = _domainManager.DeleteSearchdomain(searchdomain);
        } catch (Exception)
        {
            success = false;
            deletedEntries = 0;
        }
        return Ok(new SearchdomainDeleteResults(){Success = success, DeletedEntities = deletedEntries});
    }

    [HttpGet("Update")]
    public ActionResult<SearchdomainUpdateResults> Update(string searchdomain, string newName, string settings = "{}")
    {
        Searchdomain searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        Dictionary<string, dynamic> parameters = new()
        {
            {"name", newName},
            {"settings", settings},
            {"id", searchdomain_.id}
        };
        searchdomain_.ExecuteSQLNonQuery("UPDATE searchdomain set name = @name, settings = @settings WHERE id = @id", parameters);
        return Ok(new SearchdomainUpdateResults(){Success = true});
    }
}

using Microsoft.AspNetCore.Mvc;
using Server.Models;

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
        var results = _domainManager.ListSearchdomains()
            ?? throw new Exception("Unable to list searchdomains");
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
            return Ok(new SearchdomainCreateResults(){Id = null, Success = false});
        }
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
        } catch (Exception ex)
        {
            Console.WriteLine(ex);
            success = false;
            deletedEntries = 0;
        }
        return Ok(new SearchdomainDeleteResults(){Success = success, DeletedEntities = deletedEntries});
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
        } catch (Exception)
        {
            return Ok(new SearchdomainUpdateResults(){Success = false});
        }
        return Ok(new SearchdomainUpdateResults(){Success = true});
    }
}

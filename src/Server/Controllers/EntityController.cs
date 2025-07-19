using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Shared.Models;
using Server.Helper;
namespace Server.Controllers;

[ApiController]
[Route("[controller]")]
public class EntityController : ControllerBase
{
    private readonly ILogger<EntityController> _logger;
    private readonly IConfiguration _config;
    private SearchdomainManager _domainManager;

    public EntityController(ILogger<EntityController> logger, IConfiguration config, SearchdomainManager domainManager)
    {
        _logger = logger;
        _config = config;
        _domainManager = domainManager;
    }

    [HttpGet("Query")]
    public ActionResult<EntityQueryResults> Query(string searchdomain, string query)
    {
        Searchdomain searchdomain_;
        try
        {
            searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        } catch (Exception)
        {
            _logger.LogError("Unable to retrieve the searchdomain {searchdomain} - it likely does not exist yet", [searchdomain]); // TODO DRY violation; perhaps move this logging to the SearchdomainManager?
            return Ok(new EntityQueryResults() {Results = []});
        }
        var results = searchdomain_.Search(query);
        List<EntityQueryResult> queryResults = [.. results.Select(r => new EntityQueryResult
        {
            Name = r.Item2,
            Value = r.Item1
        })];
        return Ok(new EntityQueryResults(){Results = queryResults});
    }

    [HttpPost("Index")]
    public ActionResult<EntityIndexResult> Index([FromBody] List<JSONEntity>? jsonEntities)
    {
        List<Entity>? entities = SearchdomainHelper.EntitiesFromJSON(
            [],
            _domainManager.embeddingCache,
            _domainManager.aIProvider,
            _domainManager.helper,
            _logger,
            JsonSerializer.Serialize(jsonEntities));
        if (entities is not null && jsonEntities is not null)
        {
            List<string> invalidatedSearchdomains = [];
            foreach (var jsonEntity in jsonEntities)
            {
                string jsonEntityName = jsonEntity.Name;
                string jsonEntitySearchdomainName = jsonEntity.Searchdomain;
                if (entities.Select(x => x.name == jsonEntityName).Any()
                    && !invalidatedSearchdomains.Contains(jsonEntitySearchdomainName))
                {
                    invalidatedSearchdomains.Add(jsonEntitySearchdomainName);
                    _domainManager.InvalidateSearchdomainCache(jsonEntitySearchdomainName);
                }
            }
            return Ok(new EntityIndexResult() { Success = true });
        }
        else
        {
            _logger.LogError("Unable to deserialize an entity");
        }

        return Ok(new EntityIndexResult() { Success = false });
    }

    [HttpGet("List")]
    public ActionResult<EntityListResults> List(string searchdomain, bool returnEmbeddings = false)
    {
        Searchdomain searchdomain_;
        try
        {
            searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        } catch (Exception)
        {
            _logger.LogError("Unable to retrieve the searchdomain {searchdomain} - it likely does not exist yet", [searchdomain]);
            return Ok(new EntityListResults() { Results = [], Success = false });
        }
        EntityListResults entityListResults = new() {Results = [], Success = true};
        foreach (Entity entity in searchdomain_.entityCache)
        {
            List<AttributeResult> attributeResults = [];
            foreach (KeyValuePair<string, string> attribute in entity.attributes)
            {
                attributeResults.Add(new AttributeResult() {Name = attribute.Key, Value = attribute.Value});
            }
            List<DatapointResult> datapointResults = [];
            foreach (Datapoint datapoint in entity.datapoints)
            {
                if (returnEmbeddings)
                {
                    List<EmbeddingResult> embeddingResults = [];
                    foreach ((string, float[]) embedding in datapoint.embeddings)
                    {
                        embeddingResults.Add(new EmbeddingResult() {Model = embedding.Item1, Embeddings = embedding.Item2});
                    }
                    datapointResults.Add(new DatapointResult() {Name = datapoint.name, ProbMethod = datapoint.probMethod.name, Embeddings = embeddingResults});
                }
                else
                {
                    datapointResults.Add(new DatapointResult() {Name = datapoint.name, ProbMethod = datapoint.probMethod.name, Embeddings = null});
                }
            }
            EntityListResult entityListResult = new()
            {
                Name = entity.name,
                Attributes = attributeResults,
                Datapoints = datapointResults
            };
            entityListResults.Results.Add(entityListResult);
        }
        return Ok(entityListResults);
    }

    [HttpGet("Delete")]
    public ActionResult<EntityDeleteResults> Delete(string searchdomain, string entityName)
    {
        Entity? entity_ = SearchdomainHelper.CacheGetEntity([], entityName);
        if (entity_ is null)
        {
            _logger.LogError("Unable to delete the entity {entityName} in {searchdomain} - it was not found under the specified name", [entityName, searchdomain]);
            return Ok(new EntityDeleteResults() {Success = false});
        }
        DatabaseHelper.RemoveEntity([], _domainManager.helper, entityName, searchdomain);
        return Ok(new EntityDeleteResults() {Success = true});
    }
}

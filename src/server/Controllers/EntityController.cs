using Microsoft.AspNetCore.Mvc;
using embeddingsearch;
using System.Text.Json;
using server.Models;

namespace server.Controllers;

[ApiController]
[Route("[controller]")]
public class EntityController : ControllerBase
{
    private readonly ILogger<EntityController> _logger;
    private readonly IConfiguration _config;
    private SearchomainManager _domainManager;

    public EntityController(ILogger<EntityController> logger, IConfiguration config, SearchomainManager domainManager)
    {
        _logger = logger;
        _config = config;
        _domainManager = domainManager;
    }

    [HttpGet("Query")]
    public ActionResult<EntityQueryResults> Query(string searchdomain, string query)
    {
        Searchdomain searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        var results = searchdomain_.Search(query);
        List<EntityQueryResult> queryResults = [.. results.Select(r => new EntityQueryResult
        {
            Name = r.Item2,
            Value = r.Item1
        })];
        return Ok(new EntityQueryResults(){Results = queryResults});
    }

    [HttpGet("Index")]
    public ActionResult<EntityIndexResult> Index(string searchdomain, string jsonEntity)
    {
        Searchdomain searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        List<JSONEntity>? jsonEntities = JsonSerializer.Deserialize<List<JSONEntity>?>(jsonEntity);
        if (jsonEntities is not null)
        {
            
            List<Entity>? entities = searchdomain_.EntitiesFromJSON(jsonEntity);
            if (entities is not null)
            {
                return new EntityIndexResult() {Success = true};
            }
            else
            {
                _logger.LogDebug("Unable to deserialize an entity");
            }
        }
        return new EntityIndexResult() {Success = false};
    }

    [HttpGet("List")]
    public ActionResult<EntityListResults> List(string searchdomain, bool returnEmbeddings = false)
    {
        EntityListResults entityListResults = new() {Results = []};
        Searchdomain searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
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
                    datapointResults.Add(new DatapointResult() {Name = datapoint.name, ProbMethod = datapoint.probMethod.Method.Name, Embeddings = embeddingResults});
                }
                else
                {
                    datapointResults.Add(new DatapointResult() {Name = datapoint.name, ProbMethod = datapoint.probMethod.Method.Name, Embeddings = null});
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
        return entityListResults;
    }

    [HttpGet("Delete")]
    public ActionResult<EntityDeleteResults> Delete(string searchdomain, string entity) // TODO test this
    {
        Searchdomain searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        Entity? entity_ = searchdomain_.GetEntity(entity);
        if (entity_ is null)
        {
            return new EntityDeleteResults() {Success = false};
        }
        searchdomain_.DatabaseRemoveEntity(entity);
        return new EntityDeleteResults() {Success = true};
    }
}

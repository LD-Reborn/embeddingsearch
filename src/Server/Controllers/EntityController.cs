using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Nodes;
using Server.Models;
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
    public ActionResult<EntityIndexResult> Index(string searchdomain, [FromBody] List<JSONEntity>? jsonEntity)
    {
        Searchdomain searchdomain_;
        try
        {
            searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        }
        catch (Exception)
        {
            return Ok(new EntityIndexResult() { Success = false });
        }
        List<Entity>? entities = searchdomain_.EntitiesFromJSON(JsonSerializer.Serialize(jsonEntity));
        if (entities is not null)
        {
            _domainManager.InvalidateSearchdomainCache(searchdomain);
            return Ok(new EntityIndexResult() { Success = true });
        }
        else
        {
            _logger.LogDebug("Unable to deserialize an entity");
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
            return Ok(new EntityListResults() {Results = [], Success = false});
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
        return Ok(entityListResults);
    }

    [HttpGet("Delete")]
    public ActionResult<EntityDeleteResults> Delete(string searchdomain, string entityName) // TODO test this
    {
        Searchdomain searchdomain_;
        try
        {
            searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        } catch (Exception)
        {
            return Ok(new EntityDeleteResults() {Success = false});
        }
        Entity? entity_ = searchdomain_.GetEntity(entityName);
        if (entity_ is null)
        {
            return Ok(new EntityDeleteResults() {Success = false});
        }
        searchdomain_.RemoveEntity(entityName);
        return Ok(new EntityDeleteResults() {Success = true});
    }
}

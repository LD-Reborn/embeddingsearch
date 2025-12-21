using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Shared.Models;
using Server.Helper;
using Server.Exceptions;
namespace Server.Controllers;

[ApiController]
[Route("[controller]")]
public class EntityController : ControllerBase
{
    private readonly ILogger<EntityController> _logger;
    private readonly IConfiguration _config;
    private SearchdomainManager _domainManager;
    private readonly SearchdomainHelper _searchdomainHelper;
    private readonly DatabaseHelper _databaseHelper;

    public EntityController(ILogger<EntityController> logger, IConfiguration config, SearchdomainManager domainManager, SearchdomainHelper searchdomainHelper, DatabaseHelper databaseHelper)
    {
        _logger = logger;
        _config = config;
        _domainManager = domainManager;
        _searchdomainHelper = searchdomainHelper;
        _databaseHelper = databaseHelper;
    }

    [HttpGet("Query")]
    public ActionResult<EntityQueryResults> Query(string searchdomain, string query, int? topN)
    {
        Searchdomain searchdomain_;
        try
        {
            searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        } catch (SearchdomainNotFoundException)
        {
            _logger.LogError("Unable to retrieve the searchdomain {searchdomain} - it likely does not exist yet", [searchdomain]);
            return Ok(new EntityQueryResults() {Results = [], Success = false, Message = "Searchdomain not found" });
        } catch (Exception ex)
        {
            _logger.LogError("Unable to retrieve the searchdomain {searchdomain} - {ex.Message} - {ex.StackTrace}", [searchdomain, ex.Message, ex.StackTrace]);
            return Ok(new EntityQueryResults() {Results = [], Success = false, Message = "Unable to retrieve the searchdomain - it likely exists, but some other error happened." });
        }
        List<(float, string)> results = searchdomain_.Search(query, topN);
        List<EntityQueryResult> queryResults = [.. results.Select(r => new EntityQueryResult
        {
            Name = r.Item2,
            Value = r.Item1
        })];
        return Ok(new EntityQueryResults(){Results = queryResults, Success = true });
    }

    [HttpPost("Index")]
    public ActionResult<EntityIndexResult> Index([FromBody] List<JSONEntity>? jsonEntities)
    {
        try
        {
            List<Entity>? entities = _searchdomainHelper.EntitiesFromJSON(
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
                return Ok(new EntityIndexResult() { Success = false, Message = "Unable to deserialize an entity"});
            }
        } catch (Exception ex)
        {
            if (ex.InnerException is not null) ex = ex.InnerException;
            _logger.LogError("Unable to index the provided entities. {ex.Message} - {ex.StackTrace}", [ex.Message, ex.StackTrace]);
            return Ok(new EntityIndexResult() { Success = false, Message = ex.Message });
        }

    }

    [HttpGet("List")]
    public ActionResult<EntityListResults> List(string searchdomain, bool returnModels = false, bool returnEmbeddings = false)
    {
        if (returnEmbeddings && !returnModels)
        {
            _logger.LogError("Invalid request for {searchdomain} - embeddings return requested but without models - not possible!", [searchdomain]);
            return Ok(new EntityQueryResults() {Results = [], Success = false, Message = "Invalid request" });            
        }
        Searchdomain searchdomain_;
        try
        {
            searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        } catch (SearchdomainNotFoundException)
        {
            _logger.LogError("Unable to retrieve the searchdomain {searchdomain} - it likely does not exist yet", [searchdomain]);
            return Ok(new EntityQueryResults() {Results = [], Success = false, Message = "Searchdomain not found" });
        } catch (Exception ex)
        {
            _logger.LogError("Unable to retrieve the searchdomain {searchdomain} - {ex.Message} - {ex.StackTrace}", [searchdomain, ex.Message, ex.StackTrace]);
            return Ok(new EntityQueryResults() {Results = [], Success = false, Message = "Unable to retrieve the searchdomain - it likely exists, but some other error happened." });
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
                if (returnModels)
                {
                    List<EmbeddingResult> embeddingResults = [];
                    foreach ((string, float[]) embedding in datapoint.embeddings)
                    {
                        embeddingResults.Add(new EmbeddingResult() {Model = embedding.Item1, Embeddings = returnEmbeddings ? embedding.Item2 : []});
                    }
                    datapointResults.Add(new DatapointResult() {Name = datapoint.name, ProbMethod = datapoint.probMethod.name, SimilarityMethod = datapoint.similarityMethod.name, Embeddings = embeddingResults});
                }
                else
                {
                    datapointResults.Add(new DatapointResult() {Name = datapoint.name, ProbMethod = datapoint.probMethod.name, SimilarityMethod = datapoint.similarityMethod.name, Embeddings = null});
                }
            }
            EntityListResult entityListResult = new()
            {
                Name = entity.name,
                ProbMethod = entity.probMethodName,
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
        Searchdomain searchdomain_;
        try
        {
            searchdomain_ = _domainManager.GetSearchdomain(searchdomain);
        } catch (SearchdomainNotFoundException)
        {
            _logger.LogError("Unable to retrieve the searchdomain {searchdomain} - it likely does not exist yet", [searchdomain]);
            return Ok(new EntityQueryResults() {Results = [], Success = false, Message = "Searchdomain not found" });
        } catch (Exception ex)
        {
            _logger.LogError("Unable to retrieve the searchdomain {searchdomain} - {ex.Message} - {ex.StackTrace}", [searchdomain, ex.Message, ex.StackTrace]);
            return Ok(new EntityQueryResults() {Results = [], Success = false, Message = "Unable to retrieve the searchdomain - it likely exists, but some other error happened." });
        }
        
        Entity? entity_ = SearchdomainHelper.CacheGetEntity(searchdomain_.entityCache, entityName);
        if (entity_ is null)
        {
            _logger.LogError("Unable to delete the entity {entityName} in {searchdomain} - it was not found under the specified name", [entityName, searchdomain]);
            return Ok(new EntityDeleteResults() {Success = false, Message = "Entity not found"});
        }
        _databaseHelper.RemoveEntity([], _domainManager.helper, entityName, searchdomain);
        searchdomain_.entityCache.RemoveAll(entity => entity.name == entityName);
        return Ok(new EntityDeleteResults() {Success = true});
    }
}

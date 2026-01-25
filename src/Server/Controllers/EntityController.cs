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

    /// <summary>
    /// List the entities in a searchdomain
    /// </summary>
    /// <remarks>
    /// With returnModels = false expect: "Datapoints": [..., "Embeddings": null]<br/>
    /// With returnModels = true expect: "Datapoints": [..., "Embeddings": [{"Model": "...", "Embeddings": []}, ...]]<br/>
    /// With returnEmbeddings = true expect: "Datapoints": [..., "Embeddings": [{"Model": "...", "Embeddings": [0.007384672,0.01309805,0.0012528514,...]}, ...]]
    /// </remarks>
    /// <param name="searchdomain">Name of the searchdomain</param>
    /// <param name="returnModels">Include the models in the response</param>
    /// <param name="returnEmbeddings">Include the embeddings in the response (requires returnModels)</param>
    [HttpGet("/Entities")]
    public ActionResult<EntityListResults> List(string searchdomain, bool returnModels = false, bool returnEmbeddings = false)
    {
        if (returnEmbeddings && !returnModels)
        {
            _logger.LogError("Invalid request for {searchdomain} - embeddings return requested but without models - not possible!", [searchdomain]);
            return BadRequest(new EntityListResults() {Results = [], Success = false, Message = "Invalid request" });            
        }
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
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

    /// <summary>
    /// Index entities
    /// </summary>
    /// <remarks>
    /// Behavior: Creates new entities, but overwrites existing entities that have the same name
    /// </remarks>
    /// <param name="jsonEntities">Entities to index</param>
    [HttpPut("/Entities")]
    public ActionResult<EntityIndexResult> Index([FromBody] List<JSONEntity>? jsonEntities)
    {
        try
        {
            List<Entity>? entities = _searchdomainHelper.EntitiesFromJSON(
                _domainManager,
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
                    }
                }
                return Ok(new EntityIndexResult() { Success = true });
            }
            else
            {
                _logger.LogError("Unable to deserialize an entity");
                ElmahCore.ElmahExtensions.RaiseError(new Exception("Unable to deserialize an entity"));
                return Ok(new EntityIndexResult() { Success = false, Message = "Unable to deserialize an entity"});
            }
        } catch (Exception ex)
        {
            if (ex.InnerException is not null) ex = ex.InnerException;
            _logger.LogError("Unable to index the provided entities. {ex.Message} - {ex.StackTrace}", [ex.Message, ex.StackTrace]);
            ElmahCore.ElmahExtensions.RaiseError(ex);
            return Ok(new EntityIndexResult() { Success = false, Message = ex.Message });
        }

    }

    /// <summary>
    /// Deletes an entity
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    /// <param name="entityName">Name of the entity</param>
    [HttpDelete]
    public ActionResult<EntityDeleteResults> Delete(string searchdomain, string entityName)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        
        Entity? entity_ = SearchdomainHelper.CacheGetEntity(searchdomain_.entityCache, entityName);
        if (entity_ is null)
        {
            _logger.LogError("Unable to delete the entity {entityName} in {searchdomain} - it was not found under the specified name", [entityName, searchdomain]);
            ElmahCore.ElmahExtensions.RaiseError(
                new Exception(
                    $"Unable to delete the entity {entityName} in {searchdomain} - it was not found under the specified name"
                )
            );
            return Ok(new EntityDeleteResults() {Success = false, Message = "Entity not found"});
        }
        searchdomain_.ReconciliateOrInvalidateCacheForDeletedEntity(entity_);
        _databaseHelper.RemoveEntity([], _domainManager.helper, entityName, searchdomain);
        searchdomain_.entityCache.RemoveAll(entity => entity.name == entityName);
        return Ok(new EntityDeleteResults() {Success = true});
    }
}

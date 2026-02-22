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
    private readonly Dictionary<string, EntityIndexSessionData> _sessions = [];
    private readonly object _sessionLock = new();
    private const int SessionTimeoutMinutes = 60; // TODO: remove magic number; add an optional configuration option

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
        foreach ((string _, Entity entity) in searchdomain_.EntityCache)
        {
            List<AttributeResult> attributeResults = [];
            foreach (KeyValuePair<string, string> attribute in entity.Attributes)
            {
                attributeResults.Add(new AttributeResult() {Name = attribute.Key, Value = attribute.Value});
            }
            List<DatapointResult> datapointResults = [];
            foreach (Datapoint datapoint in entity.Datapoints)
            {
                if (returnModels)
                {
                    List<EmbeddingResult> embeddingResults = [];
                    foreach ((string, float[]) embedding in datapoint.Embeddings)
                    {
                        embeddingResults.Add(new EmbeddingResult() {Model = embedding.Item1, Embeddings = returnEmbeddings ? embedding.Item2 : []});
                    }
                    datapointResults.Add(new DatapointResult() {Name = datapoint.Name, ProbMethod = datapoint.ProbMethod.Name, SimilarityMethod = datapoint.SimilarityMethod.Name, Embeddings = embeddingResults});
                }
                else
                {
                    datapointResults.Add(new DatapointResult() {Name = datapoint.Name, ProbMethod = datapoint.ProbMethod.Name, SimilarityMethod = datapoint.SimilarityMethod.Name, Embeddings = null});
                }
            }
            EntityListResult entityListResult = new()
            {
                Name = entity.Name,
                ProbMethod = entity.ProbMethodName,
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
    /// Behavior: Updates the index using the provided entities. Creates new entities, overwrites existing entities with the same name, and deletes entities that are not part of the index anymore.
    /// 
    /// Can be executed in a single request or in multiple chunks using a (self-defined) session UUID string.
    /// 
    /// For session-based chunk uploads:
    /// - Provide sessionId to accumulate entities across multiple requests
    /// - Set sessionComplete=true on the final request to finalize and delete entities that are not in the accumulated list
    /// - Without sessionId: Missing entities will be deleted from the searchdomain.
    /// - Sessions expire after 60 minutes of inactivity (or as otherwise configured in the appsettings)
    /// </remarks>
    /// <param name="jsonEntities">Entities to index</param>
    /// <param name="sessionId">Optional session ID for batch uploads across multiple requests</param>
    /// <param name="sessionComplete">If true, finalizes the session and deletes entities not in the accumulated list</param>
    [HttpPut("/Entities")]
    public async Task<ActionResult<EntityIndexResult>> Index(
        [FromBody] List<JSONEntity>? jsonEntities,
        string? sessionId = null,
        bool sessionComplete = false)
    {
        try
        {
            if (sessionId is null || string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = Guid.NewGuid().ToString(); // Create a short-lived session
                sessionComplete = true; // If no sessionId was set, there is no trackable session. The pseudo-session ends here.
            }
            // Periodic cleanup of expired sessions
            CleanupExpiredEntityIndexSessions();
            EntityIndexSessionData session = GetOrCreateEntityIndexSession(sessionId);

            if (jsonEntities is null && !sessionComplete)
            {
                return BadRequest(new EntityIndexResult() { Success = false, Message = "jsonEntities can only be null for a complete session" });
            } else if (jsonEntities is null && sessionComplete)
            {
                await EntityIndexSessionDeleteUnindexedEntities(session);
                return Ok(new EntityIndexResult() { Success = true });
            }

            // Standard entity indexing (upsert behavior)
            List<Entity>? entities = await _searchdomainHelper.EntitiesFromJSON(
                _domainManager,
                _logger,
                JsonSerializer.Serialize(jsonEntities));
            if (entities is not null && jsonEntities is not null)
            {
                session.AccumulatedEntities.AddRange(entities);

                if (sessionComplete)
                {
                    await EntityIndexSessionDeleteUnindexedEntities(session);
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

    private async Task EntityIndexSessionDeleteUnindexedEntities(EntityIndexSessionData session)
    {
        var entityGroupsBySearchdomain = session.AccumulatedEntities.GroupBy(e => e.Searchdomain);

        foreach (var entityGroup in entityGroupsBySearchdomain)
        {
            string searchdomainName = entityGroup.Key;
            var entityNamesInRequest = entityGroup.Select(e => e.Name).ToHashSet();

            (Searchdomain? searchdomain_, int? httpStatusCode, string? message) =
                SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomainName, _logger);

            if (searchdomain_ is not null && httpStatusCode is null) // If getting searchdomain was successful
            {
                var entitiesToDelete = searchdomain_.EntityCache
                    .Where(kvp => !entityNamesInRequest.Contains(kvp.Value.Name))
                    .Select(kvp => kvp.Value)
                    .ToList();

                foreach (var entity in entitiesToDelete)
                {
                    searchdomain_.ReconciliateOrInvalidateCacheForDeletedEntity(entity);
                    await _databaseHelper.RemoveEntity(
                        [],
                        _domainManager.Helper,
                        entity.Name,
                        searchdomainName);
                    searchdomain_.EntityCache.TryRemove(entity.Name, out _);
                    _logger.LogInformation("Deleted entity {entityName} from {searchdomain}", entity.Name, searchdomainName);
                }
            }
            else
            {
                _logger.LogWarning("Unable to delete entities for searchdomain {searchdomain}", searchdomainName);
            }
        }
    }

    /// <summary>
    /// Deletes an entity
    /// </summary>
    /// <param name="searchdomain">Name of the searchdomain</param>
    /// <param name="entityName">Name of the entity</param>
    [HttpDelete]
    public async Task<ActionResult<EntityDeleteResults>> Delete(string searchdomain, string entityName)
    {
        (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_domainManager, searchdomain, _logger);
        if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new SearchdomainUpdateResults(){Success = false, Message = message});
        
        Entity? entity_ = SearchdomainHelper.CacheGetEntity(searchdomain_.EntityCache, entityName);
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
        await _databaseHelper.RemoveEntity([], _domainManager.Helper, entityName, searchdomain);
        
        bool success = searchdomain_.EntityCache.TryRemove(entityName, out Entity? _);
        
        return Ok(new EntityDeleteResults() {Success = success});
    }


    private void CleanupExpiredEntityIndexSessions()
    {
        lock (_sessionLock)
        {
            var expiredSessions = _sessions
                .Where(kvp => (DateTime.UtcNow - kvp.Value.LastInteractionAt).TotalMinutes > SessionTimeoutMinutes)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var sessionId in expiredSessions)
            {
                _sessions.Remove(sessionId);
                _logger.LogWarning("Removed expired, non-closed session {sessionId}", sessionId);
            }
        }
    }

    private EntityIndexSessionData GetOrCreateEntityIndexSession(string sessionId)
    {
        lock (_sessionLock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                session = new EntityIndexSessionData();
                _sessions[sessionId] = session;
            } else
            {
                session.LastInteractionAt = DateTime.UtcNow;
            }
            return session;
        }
    }
}

public class EntityIndexSessionData
{
    public List<Entity> AccumulatedEntities { get; set; } = [];
    public DateTime LastInteractionAt { get; set; } = DateTime.UtcNow;
}
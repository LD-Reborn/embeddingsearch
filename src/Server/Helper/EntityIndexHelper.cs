using System.Text.Json;
using Shared.Models;

namespace Server.Helper;

/// <summary>
/// Helper class for entity indexing operations
/// </summary>
public class EntityIndexHelper
{
    private readonly SearchdomainHelper _searchdomainHelper;
    private readonly SearchdomainManager _domainManager;
    private readonly ILogger<EntityIndexHelper> _logger;

    public EntityIndexHelper(SearchdomainHelper searchdomainHelper, SearchdomainManager domainManager, ILogger<EntityIndexHelper> logger)
    {
        _searchdomainHelper = searchdomainHelper;
        _domainManager = domainManager;
        _logger = logger;
    }

    /// <summary>
    /// Deserializes JSON entities with standardized error handling
    /// </summary>
    /// <param name="jsonEntities">List of entities to deserialize</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <returns>Tuple of (entities, success, errorMessage)</returns>
    public async Task<(List<Entity>? Entities, bool Success, string? ErrorMessage)> DeserializeEntitiesAsync(
        List<JSONEntity>? jsonEntities,
        string operationName = "entity deserialization")
    {
        try
        {
            if (jsonEntities is null || jsonEntities.Count == 0)
            {
                return (null, false, "No entities provided");
            }

            List<Entity>? entities = await _searchdomainHelper.EntitiesFromJSON(
                _domainManager,
                _logger,
                JsonSerializer.Serialize(jsonEntities));

            if (entities is not null && entities.Count > 0)
            {
                return (entities, true, null);
            }
            else
            {
                _logger.LogError("Unable to deserialize entities during {operationName}", operationName);
                ElmahCore.ElmahExtensions.RaiseError(new Exception($"Unable to deserialize entities during {operationName}"));
                return (null, false, "Unable to deserialize entities");
            }
        }
        catch (Exception ex)
        {
            if (ex.InnerException is not null) ex = ex.InnerException;
            _logger.LogError("Error during {operationName}: {ex.Message} - {ex.StackTrace}", operationName, ex.Message, ex.StackTrace);
            ElmahCore.ElmahExtensions.RaiseError(ex);
            return (null, false, ex.Message);
        }
    }
}

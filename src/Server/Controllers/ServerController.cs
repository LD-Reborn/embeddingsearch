namespace Server.Controllers;

using ElmahCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Server.Helper;
using Server.Models;
using Shared;
using Shared.Models;

[ApiController]
[Route("[controller]")]
public class ServerController : ControllerBase
{
    private readonly ILogger<ServerController> _logger;
    private readonly IConfiguration _config;
    private AIProvider _aIProvider;
    private readonly SearchdomainManager _searchdomainManager;
    private readonly IOptions<EmbeddingSearchOptions> _options;

    public ServerController(ILogger<ServerController> logger, IConfiguration config, AIProvider aIProvider, SearchdomainManager searchdomainManager, IOptions<EmbeddingSearchOptions> options)
    {
        _logger = logger;
        _config = config;
        _aIProvider = aIProvider;
        _searchdomainManager = searchdomainManager;
        _options = options;
    }

    /// <summary>
    /// Lists the models available to the server
    /// </summary>
    /// <remarks>
    /// Returns ALL models available to the server - not only the embedding models.
    /// </remarks>
    [HttpGet("Models")]
    public ActionResult<ServerGetModelsResult> GetModels()
    {
        try
        {
            string[] models = _aIProvider.GetModels();
            return new ServerGetModelsResult() { Models = models, Success = true };
        } catch (Exception ex)
        {
            _logger.LogError("Unable to get models due to exception {ex.Message} - {ex.StackTrace}", [ex.Message, ex.StackTrace]);
            return new ServerGetModelsResult() { Success = false, Message = ex.Message};
        }
    }

    /// <summary>
    /// Gets numeric info regarding the searchdomains
    /// </summary>
    [HttpGet("Stats")]
    public async Task<ActionResult<ServerGetStatsResult>> Stats()
    {
        try
        {
            long size = 0;
            long elementCount = 0;
            long embeddingsCount = 0;
            EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache = _searchdomainManager.embeddingCache;

            foreach (KeyValuePair<string, Dictionary<string, float[]>> kv in embeddingCache)
            {
                string key = kv.Key;
                Dictionary<string, float[]> entry = kv.Value;
                size += EstimateEntrySize(key, entry);
                elementCount++;
                embeddingsCount += entry.Keys.Count;
            }
            var sqlHelper = DatabaseHelper.GetSQLHelper(_options.Value);
            var databaseTotalSize = DatabaseHelper.GetTotalDatabaseSize(sqlHelper);
            Task<long> entityCountTask = DatabaseHelper.CountEntities(sqlHelper);
            long queryCacheUtilization = 0;
            long queryCacheElementCount = 0;
            long queryCacheMaxElementCountAll = 0;
            long queryCacheMaxElementCountLoadedSearchdomainsOnly = 0;
            foreach (string searchdomain in await _searchdomainManager.ListSearchdomainsAsync())
            {
                if (SearchdomainHelper.IsSearchdomainLoaded(_searchdomainManager, searchdomain))
                {
                    (Searchdomain? searchdomain_, int? httpStatusCode, string? message) = SearchdomainHelper.TryGetSearchdomain(_searchdomainManager, searchdomain, _logger);
                    if (searchdomain_ is null || httpStatusCode is not null) return StatusCode(httpStatusCode ?? 500, new ServerGetStatsResult(){Success = false, Message = message});
                    queryCacheUtilization += searchdomain_.GetSearchCacheSize();
                    queryCacheElementCount += searchdomain_.queryCache.Count;
                    queryCacheMaxElementCountAll += searchdomain_.queryCache.Capacity;
                    queryCacheMaxElementCountLoadedSearchdomainsOnly += searchdomain_.queryCache.Capacity;
                } else
                {
                    var searchdomainSettings = DatabaseHelper.GetSearchdomainSettings(sqlHelper, searchdomain);
                    queryCacheMaxElementCountAll += searchdomainSettings.QueryCacheSize;
                }
            };
            long entityCount = await entityCountTask;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long ramTotalSize = GC.GetTotalMemory(false);
            
            return new ServerGetStatsResult() {
                Success = true,
                EntityCount = entityCount,
                QueryCacheUtilization = queryCacheUtilization,
                QueryCacheElementCount = queryCacheElementCount,
                QueryCacheMaxElementCountAll = queryCacheMaxElementCountAll,
                QueryCacheMaxElementCountLoadedSearchdomainsOnly = queryCacheMaxElementCountLoadedSearchdomainsOnly,
                EmbeddingCacheUtilization = size,
                EmbeddingCacheMaxElementCount = _searchdomainManager.EmbeddingCacheMaxCount,
                EmbeddingCacheElementCount = elementCount,
                EmbeddingsCount = embeddingsCount,
                DatabaseTotalSize = databaseTotalSize,
                RamTotalSize = ramTotalSize
            };
        } catch (Exception ex)
        {
            ElmahExtensions.RaiseError(ex);
            return StatusCode(500, new ServerGetStatsResult(){Success = false, Message = ex.Message});
        }
    }

    private static long EstimateEntrySize(string key, Dictionary<string, float[]> value)
    {
        int stringOverhead = MemorySizes.Align(MemorySizes.ObjectHeader + sizeof(int));
        int arrayOverhead = MemorySizes.ArrayHeader;
        int dictionaryOverhead = MemorySizes.ObjectHeader;
        long size = 0;

        size += stringOverhead + key.Length * sizeof(char);
        size += dictionaryOverhead;

        foreach (var kv in value)
        {
            size += stringOverhead + kv.Key.Length * sizeof(char);
            size += arrayOverhead + kv.Value.Length * sizeof(float);
        }

        return size;
    }
}

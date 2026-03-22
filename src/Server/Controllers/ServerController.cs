namespace Server.Controllers;

using System.Text.RegularExpressions;
using ElmahCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Server.Helper;
using Server.Models;
using Shared;
using Shared.Helper;
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
    private readonly IHostEnvironment _hostEnvironment;

    public ServerController(ILogger<ServerController> logger, IConfiguration config, AIProvider aIProvider, SearchdomainManager searchdomainManager, IOptions<EmbeddingSearchOptions> options, IHostEnvironment hostEnvironment)
    {
        _logger = logger;
        _config = config;
        _aIProvider = aIProvider;
        _searchdomainManager = searchdomainManager;
        _options = options;
        _hostEnvironment = hostEnvironment;
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
            EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache = _searchdomainManager.EmbeddingCache;
            (long embeddingCacheUtilization, long embeddingCacheElementCount, long embeddingCacheEmbeddingsCount) = CacheHelper.EstimateCacheSize(embeddingCache);

            var sqlHelper = _searchdomainManager.Helper;
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
                    queryCacheElementCount += searchdomain_.QueryCache.Count;
                    queryCacheMaxElementCountAll += searchdomain_.QueryCache.Capacity;
                    queryCacheMaxElementCountLoadedSearchdomainsOnly += searchdomain_.QueryCache.Capacity;
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
                EmbeddingCacheUtilization = embeddingCacheUtilization,
                EmbeddingCacheMaxElementCount = _searchdomainManager.EmbeddingCacheMaxCount,
                EmbeddingCacheElementCount = embeddingCacheElementCount,
                EmbeddingsCount = embeddingCacheEmbeddingsCount,
                DatabaseTotalSize = databaseTotalSize,
                RamTotalSize = ramTotalSize
            };
        } catch (Exception ex)
        {
            ElmahExtensions.RaiseError(ex);
            return StatusCode(500, new ServerGetStatsResult(){Success = false, Message = ex.Message});
        }
    }

    /// <summary>
    /// Evicts elements from the EmbeddingCache until it conforms to the given size
    /// </summary>
    /// <param name="targetSize">Target size in bytes</param>
    [Authorize]
    [HttpPost("EvictEmbeddingCacheToSize")]
    public ActionResult<ServerEvictEmbeddingCacheToSizeResult> EvictEmbeddingCacheToSize([FromBody] long targetSize)
    {
        EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache = _searchdomainManager.EmbeddingCache;
        (long embeddingCacheUtilization, long embeddingCacheElementCount, long _) = CacheHelper.EstimateCacheSize(embeddingCache);
        if (embeddingCacheUtilization <= targetSize)
        {
            return Ok(new ServerEvictEmbeddingCacheToSizeResult() {Success = true, EvictedElements = 0});
        }
        foreach (KeyValuePair<string, Dictionary<string, float[]>> element in embeddingCache)
        {
            string key = element.Key;
            Dictionary<string, float[]> entry = element.Value;
            embeddingCacheUtilization -= CacheHelper.EstimateEntrySize(key, entry);
            embeddingCacheElementCount--;
            if (embeddingCacheUtilization <= targetSize)
            {
                break;
            }
        }
        long evictedElements = embeddingCache.Count - embeddingCacheElementCount;
        embeddingCache.Capacity = (int)embeddingCacheElementCount;
        return Ok(new ServerEvictEmbeddingCacheToSizeResult() {Success = true, EvictedElements = evictedElements});
    }

    /// <summary>
    /// Sets the EmbeddingCache size and auto-evict superfluous elements
    /// </summary>
    /// <param name="size">Target size in element count</param>
    [Authorize]
    [HttpPost("SetEmbeddingCacheSize")]
    public ActionResult<ServerSetEmbeddingCacheSizeResult> SetEmbeddingCacheSize([FromBody] long size)
    {
        _searchdomainManager.EmbeddingCacheMaxCount = size;
        long evictedCount = Math.Max(0, _searchdomainManager.EmbeddingCache.Capacity - size);
        _searchdomainManager.EmbeddingCache.Capacity = (int)size;
        _options.Value.Cache.CacheTopN = size;
        ConfigHelper.UpdateSetting(_hostEnvironment, "Embeddingsearch:Cache:CacheTopN", size);
        return Ok(new ServerSetEmbeddingCacheSizeResult() { Success = true, EvictedElements = evictedCount });
    }

    /// <summary>
    /// Removes a given model from all elements in the EmbeddingCache
    /// </summary>
    /// <param name="model">Model to remove from Embeddingcache</param>
    [Authorize]
    [HttpPost("RemoveModelFromEmbeddingCache")]
    public ActionResult<ServerRemoveModelFromEmbeddingCacheResult> RemoveModelFromEmbeddingCache([FromBody] string model)
    {
        long evictedCount = 0;
        foreach (KeyValuePair<string, Dictionary<string, float[]>> element in _searchdomainManager.EmbeddingCache)
        {
            Dictionary<string, float[]> entry = element.Value;
            if (entry.Remove(model))
            {
                evictedCount += 1;
            }
        }
        return Ok(new ServerRemoveModelFromEmbeddingCacheResult() { Success = true, EvictedElements = evictedCount });
    }

    /// <summary>
    /// Outputs the EmbeddingCache
    /// </summary>
    /// <param name="filter">Regex filter (case-insensitive) to restrict the selection</param>
    [HttpGet("EmbeddingCache")]
    public ActionResult<ServerGetEmbeddingCacheResult> EmbeddingCache(string? filter)
    {
        filter ??= ".*";
        var regex = new Regex(filter, RegexOptions.IgnoreCase);
        List<KeyValuePair<string, List<string>>> result = [];
        foreach (KeyValuePair<string, Dictionary<string, float[]>> element in _searchdomainManager.EmbeddingCache)
        {
            if (regex.IsMatch(element.Key))
            {
                List<string> elements = [.. element.Value.Select(x => x.Key)];
                result.Add(new(element.Key, elements));
            }
        }
        return new ServerGetEmbeddingCacheResult() {Success = true, EmbeddingCache = result};
    }

    /// <summary>
    /// Evicts entries from the EmbeddingCache
    /// </summary>
    /// <param name="filter">Regex filter (case-insensitive) to select which entries to evict</param>
    [HttpDelete("EmbeddingCache")]
    public ActionResult<ServerEvictEmbeddingCacheResult> EvictEmbeddingCache(string? filter)
    {
        filter ??= ".*";
        var regex = new Regex(filter, RegexOptions.IgnoreCase);
        List<string> toBeDeleted = [];
        foreach (KeyValuePair<string, Dictionary<string, float[]>> element in _searchdomainManager.EmbeddingCache)
        {
            if (regex.IsMatch(element.Key))
            {
                toBeDeleted.Add(element.Key);
            }
        }
        toBeDeleted.ForEach(element => _searchdomainManager.EmbeddingCache.Remove(element));
        return new ServerEvictEmbeddingCacheResult() {Success = true, EvictedElements = toBeDeleted.Count};
    }

    /// <summary>
    /// Evicts entries from the EmbeddingCache that contain the listed models (and only them)
    /// </summary>
    /// <param name="dryRun">if set to true, the number of affected elements is returned without actually evicting them</param>
    /// <param name="models">model combination</param>
    [HttpDelete("EvictFromEmbeddingCacheByModels")]
    public ActionResult<ServerEvictEmbeddingCacheResult> EvictFromEmbeddingCacheByModels(bool? dryRun, [FromBody]List<string> models)
    {
        if (models.Count == 0)
        {
            return BadRequest(new ServerEvictEmbeddingCacheResult() {Success = false, EvictedElements = 0});
        }

        List<string> toBeDeleted = [];

        foreach (KeyValuePair<string, Dictionary<string, float[]>> element in _searchdomainManager.EmbeddingCache)
        {
            string cacheKey = element.Key;
            var modelDict = element.Value;
            if (modelDict.Count == models.Count
                && modelDict.Keys.All(models.Contains)
                && models.All(modelDict.Keys.Contains))
            {
                toBeDeleted.Add(element.Key);
            }
        }
        if (dryRun != true)
        {
            toBeDeleted.ForEach(key => _searchdomainManager.EmbeddingCache.Remove(key));
        }
        return new ServerEvictEmbeddingCacheResult() {Success = true, EvictedElements = toBeDeleted.Count};
    }
}

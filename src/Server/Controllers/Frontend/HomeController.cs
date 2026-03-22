using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Helper;
using Shared.Models;
using Server.Exceptions;
using Server.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Shared;
namespace Server.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
[Route("[Controller]")]
public class HomeController : Controller
{
    private readonly ILogger<EntityController> _logger;
    private readonly SearchdomainManager _domainManager;

    public HomeController(ILogger<EntityController> logger, IConfiguration config, SearchdomainManager domainManager, SearchdomainHelper searchdomainHelper, DatabaseHelper databaseHelper)
    {
        _logger = logger;
        _domainManager = domainManager;
    }

    [HttpGet("/")]
    public IActionResult Root()
    {
        return Redirect("/Home/Index");
    }

    [Authorize]
    [HttpGet("Index")]
    public IActionResult Index()
    {
        return View();
    }

    [Authorize]
    [HttpGet("Searchdomains")]
    public async Task<ActionResult> Searchdomains()
    {
        HomeSearchdomainsViewModel viewModel = new()
        {
            Searchdomains = await _domainManager.ListSearchdomainsAsync()
        };
        return View(viewModel);
    }

    [Authorize]
    [HttpGet("Cache")]
    public async Task<ActionResult> Cache()
    {
        var embeddingCache = _domainManager.EmbeddingCache;
        long embeddingCacheSize = 0;
        long embeddingCacheElementCount = 0;
        long embeddingCacheEmbeddingCount = 0;
        Dictionary<string, (long entryCount, long databaseSize)> perModelStats = [];
        List<string> models = [];
        List<List<string>> modelCombinations = [];
        foreach (KeyValuePair<string, Dictionary<string, float[]>> kv in embeddingCache)
        {
            string key = kv.Key;
            Dictionary<string, float[]> entry = kv.Value;
            embeddingCacheSize += CacheHelper.EstimateEntrySize(key, entry);
            embeddingCacheElementCount++;
            embeddingCacheEmbeddingCount += entry.Keys.Count;
            float entryBucketCountTemp = MemorySizes.GetBucketCount(entry);
            float entryBucketPerElementRatio = entryBucketCountTemp / entry.Count;
            float carryOver = 0;
            Stack<int> entryBucketPerElementCounts = [];
            while (entryBucketCountTemp >= entryBucketPerElementRatio)
            {
                carryOver += entryBucketPerElementRatio;
                int pushValue = (int)carryOver;
                entryBucketPerElementCounts.Push(pushValue);
                carryOver -= pushValue;
                entryBucketCountTemp -= entryBucketPerElementRatio;
            }
            List<string> modelCombination = [];
            foreach (KeyValuePair<string, float[]> modelKV in entry)
            {
                string model = modelKV.Key;
                if (!perModelStats.ContainsKey(model))
                {
                    perModelStats[model] = (0, 0);
                }
                (long entryCount, long databaseSize) = perModelStats[model];
                entryCount += 1;
                long entryKvElementSize = MemorySizes.EstimateKeyValuePairContainerSize(modelKV, entry);
                long entryKvKeySize = MemorySizes.GetStringSize(modelKV.Key);
                long entryKvValueSize = MemorySizes.GetFloatArraySize(modelKV.Value);

                databaseSize +=
                    entryKvElementSize +
                    entryKvValueSize +
                    entryKvKeySize;
                
                perModelStats[model] = (entryCount, databaseSize);

                if (!models.Contains(model))
                {
                    models.Add(model);
                }
                if (!modelCombination.Contains(model))
                {
                    modelCombination.Add(model);
                }
            }
            modelCombination.Sort();
            bool existsCombination = modelCombinations.Any(c => c.SequenceEqual(modelCombination));
            if (!existsCombination)
            {
                modelCombinations.Add(modelCombination);
            }
        }
        HomeCacheViewModel viewModel = new()
        {
            PerModelStats = perModelStats,
            EmbeddingCacheCount = embeddingCacheElementCount,
            EmbeddingCacheEmbeddingCount = embeddingCacheEmbeddingCount,
            EmbeddingCacheDatabaseSize = embeddingCacheSize,
            EmbeddingCacheDatabaseMaxSize = _domainManager.EmbeddingCacheMaxCount,
            Models = models,
            ModelCombinations = modelCombinations
        };
        return View(viewModel);
    }
}
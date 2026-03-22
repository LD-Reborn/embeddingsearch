namespace Server.Models;

public class HomeSearchdomainsViewModel
{
    public required List<string> Searchdomains { get; set; }
}

public class HomeCacheViewModel
{
    public required Dictionary<string, (long entryCount, long databaseSize)> PerModelStats { get; set; }
    public required long EmbeddingCacheCount { get; set; }
    public required long EmbeddingCacheDatabaseSize { get; set; }
    public required long EmbeddingCacheDatabaseMaxSize { get; set; }
    public required long EmbeddingCacheEmbeddingCount { get; set; }
    public required List<string> Models { get; set; }
    public required List<List<string>> ModelCombinations { get; set; }
}
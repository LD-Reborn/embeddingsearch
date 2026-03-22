
using System.Reflection;
using System.Text.Json.Serialization;

namespace Shared.Models;
public readonly struct ResultItem
{
    [JsonPropertyName("Score")]
    public readonly float Score { get; }
    [JsonPropertyName("Name")]
    public readonly string Name { get; }

    [JsonConstructor]
    public ResultItem(float score, string name)
    {
        Score = score;
        Name = name;
    }

    public static long EstimateSize(ResultItem item)
    {
        long size = 0;

        // string object
        if (item.Name != null)
        {
            size += MemorySizes.ObjectHeader;
            size += sizeof(int); // string length
            size += item.Name.Length * sizeof(char);
            size = Align(size);
        }

        return size;
    }

    private static long Align(long size)
        => (size + 7) & ~7; // 8-byte alignment
}

public struct DateTimedSearchResult(DateTime dateTime, List<ResultItem> results)
{
    [JsonPropertyName("AccessDateTimes")]
    public List<DateTime> AccessDateTimes { get; set; } = [dateTime];
    [JsonPropertyName("Results")]
    public List<ResultItem> Results { get; set; } = results;

    public long EstimateSize()
    {
        long size = 0;

        size += EstimateDateTimeList(AccessDateTimes);
        size += EstimateResultItemList(Results);

        return size;
    }

    private static long EstimateDateTimeList(List<DateTime>? list)
    {
        if (list == null)
            return 0;

        long size = 0;

        // List object
        size += MemorySizes.ObjectHeader;
        size += MemorySizes.Reference; // reference to array

        // Internal array
        size += MemorySizes.ArrayHeader;
        size += list.Capacity * sizeof(long); // DateTime = 8 bytes

        return size;
    }

    private static long EstimateResultItemList(List<ResultItem>? list)
    {
        if (list == null)
            return 0;

        long size = 0;

        // List object
        size += MemorySizes.ObjectHeader;
        size += MemorySizes.Reference;

        // Internal array of structs
        size += MemorySizes.ArrayHeader;
        int resultItemInlineSize = sizeof(float) + IntPtr.Size; // float + string reference
        size += list.Capacity * resultItemInlineSize;

        // Heap allocations referenced by ResultItem
        foreach (var item in list)
            size += ResultItem.EstimateSize(item);

        return size;
    }
}

public struct SearchdomainSettings(bool cacheReconciliation = false, int queryCacheSize = 1_000_000, bool parallelEmbeddingsPrefetch = false)
{
    [JsonPropertyName("CacheReconciliation")]
    public bool CacheReconciliation { get; set; } = cacheReconciliation;
    [JsonPropertyName("QueryCacheSize")]
    public int QueryCacheSize { get; set; } = queryCacheSize;
    [JsonPropertyName("ParallelEmbeddingsPrefetch")]
    public bool ParallelEmbeddingsPrefetch { get; set; } = parallelEmbeddingsPrefetch;
}

public static class MemorySizes
{
    public static readonly int PointerSize = IntPtr.Size;
    public static readonly int ObjectHeader = PointerSize * 2;
    public static readonly int Reference = PointerSize;
    public static readonly int ArrayHeader = Align(ObjectHeader + sizeof(int));

    public static int Align(int size)
        => (size + PointerSize - 1) & ~(PointerSize - 1);
    
    public static long GetFloatArraySize(float[] floats)
        => Align(ArrayHeader + floats.Length * sizeof(float));
    
    public static long GetStringSize(string value)
        => Align(
            ObjectHeader +
            sizeof(int) +
            sizeof(char) * (value.Length + 1) // calculate the length including the null terminator
        );

    public static long EstimateKeyValuePairContainerSize<TKey, TValue>(this KeyValuePair<TKey, TValue> kv, Dictionary<TKey, TValue> dict) where TKey : notnull
    { // Estimates size of container only! Calculate TKey and TValue separately!
        return Align(
                    sizeof(int) + // hashCode
                    sizeof(int) + // next
                    Reference + // string reference
                    Reference // float[] reference
                ) + (sizeof(int) * GetBucketCount(dict) / dict.Count); // Spread allocated bucket size across actually used dict element count.
    }                                                                  // Floor rounding could cause compounding errors (+-1 every time), but good enough for an estimation.
    
    public static int GetBucketCount<TKey, TValue>(this Dictionary<TKey, TValue> dict) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(dict);
        // Try to get the private field that stores the bucket array
        var bucketsField = (typeof(Dictionary<TKey, TValue>)
            .GetField("_buckets", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? typeof(Dictionary<TKey, TValue>)
            .GetField("buckets", BindingFlags.NonPublic | BindingFlags.Instance))
            ?? throw new NotSupportedException("Cannot find the internal bucket field.");
        var buckets = bucketsField.GetValue(dict) as int[];
        return buckets?.Length ?? 0;
    }

}
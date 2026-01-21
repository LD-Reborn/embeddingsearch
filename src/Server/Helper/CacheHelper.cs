using System.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using OllamaSharp.Models;
using Server.Models;
using Shared;

namespace Server.Helper;

public static class CacheHelper
{
    public static EnumerableLruCache<string, Dictionary<string, float[]>> GetEmbeddingStore(EmbeddingSearchOptions options)
    {
        SQLiteHelper helper = new(options);
        EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache = new((int)(options.Cache.StoreTopN ?? options.Cache.CacheTopN));
        helper.ExecuteQuery(
            "SELECT cache_key, model_key, embedding, idx FROM embedding_cache ORDER BY idx ASC", [], r =>
            {
                int embeddingOrdinal = r.GetOrdinal("embedding");
                int length = (int)r.GetBytes(embeddingOrdinal, 0, null, 0, 0);
                byte[] buffer = new byte[length];
                r.GetBytes(embeddingOrdinal, 0, buffer, 0, length);
                var cache_key = r.GetString(r.GetOrdinal("cache_key"));
                var model_key = r.GetString(r.GetOrdinal("model_key"));
                var embedding = SearchdomainHelper.FloatArrayFromBytes(buffer);
                var index = r.GetInt32(r.GetOrdinal("idx"));
                if (cache_key is null || model_key is null || embedding is null)
                {
                    throw new Exception("Unable to get the embedding store due to a returned element being null");
                }
                if (!embeddingCache.TryGetValue(cache_key, out Dictionary<string, float[]>? keyElement) || keyElement is null)
                {
                    keyElement = [];
                    embeddingCache[cache_key] = keyElement;
                }
                keyElement[model_key] = embedding;
                return 0;
            }
        );
        embeddingCache.Capacity = (int)options.Cache.CacheTopN;
        return embeddingCache;
    }

    public static async Task UpdateEmbeddingStore(EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache, EmbeddingSearchOptions options)
    {
        if (options.Cache.StoreTopN is not null)
        {
            embeddingCache.Capacity = (int)options.Cache.StoreTopN;
        }
        SQLiteHelper helper = new(options);
        EnumerableLruCache<string, Dictionary<string, float[]>> embeddingStore = GetEmbeddingStore(options);


        var embeddingCacheMappings = GetCacheMappings(embeddingCache);
        var embeddingCacheIndexMap = embeddingCacheMappings.positionToEntry;
        var embeddingCacheObjectMap = embeddingCacheMappings.entryToPosition;

        var embeddingStoreMappings = GetCacheMappings(embeddingStore);
        var embeddingStoreIndexMap = embeddingStoreMappings.positionToEntry;
        var embeddingStoreObjectMap = embeddingStoreMappings.entryToPosition;

        List<int> deletedEntries = [];

        foreach (KeyValuePair<int, KeyValuePair<string, Dictionary<string, float[]>>> kv in embeddingStoreIndexMap)
        {
            int storeEntryIndex = kv.Key;
            string storeEntryString = kv.Value.Key;
            bool cacheEntryExists = embeddingCacheObjectMap.TryGetValue(storeEntryString, out int cacheEntryIndex);
            
            if (!cacheEntryExists) // Deleted
            {
                deletedEntries.Add(storeEntryIndex);
            }
        }
        Task removeEntriesFromStoreTask = RemoveEntriesFromStore(helper, deletedEntries);


        List<(int Index, KeyValuePair<string, Dictionary<string, float[]>> Entry)> createdEntries = [];
        List<(int Index, int NewIndex)> changedEntries = [];
        List<(int Index, string Model, string Key, float[] Embedding)> AddedModels = [];
        List<(int Index, string Model)> RemovedModels = [];
        foreach (KeyValuePair<int, KeyValuePair<string, Dictionary<string, float[]>>> kv in embeddingCacheIndexMap)
        {
            int cacheEntryIndex = kv.Key;
            string cacheEntryString = kv.Value.Key;

            bool storeEntryExists = embeddingStoreObjectMap.TryGetValue(cacheEntryString, out int storeEntryIndex);

            if (!storeEntryExists) // Created
            {
                createdEntries.Add((
                    Index: cacheEntryIndex,
                    Entry: kv.Value
                ));
                continue;
            }
            if (cacheEntryIndex != storeEntryIndex) // Changed
            {
                changedEntries.Add((
                    Index: cacheEntryIndex,
                    NewIndex: storeEntryIndex
                ));
            }

            // Check for new/removed models
            var storeModels = embeddingStoreIndexMap[storeEntryIndex].Value;
            var cacheModels = kv.Value.Value;
            // New models
            foreach (var model in storeModels.Keys.Except(cacheModels.Keys))
            {
                RemovedModels.Add((
                    Index: cacheEntryIndex,
                    Model: model
                ));
            }
            // Removed models
            foreach (var model in cacheModels.Keys.Except(storeModels.Keys))
            {
                AddedModels.Add((
                    Index: cacheEntryIndex,
                    Model: model,
                    Key: cacheEntryString,
                    Embedding: cacheModels[model]
                ));
            }
        }

        var taskSet = new List<Task>
        {
            removeEntriesFromStoreTask,
            CreateEntriesInStore(helper, createdEntries),
            UpdateEntryIndicesInStore(helper, changedEntries),
            AddModelsToIndices(helper, AddedModels),
            RemoveModelsFromIndices(helper, RemovedModels)
        };

        await Task.WhenAll(taskSet);
    }

    private static async Task CreateEntriesInStore(
        SQLiteHelper helper,
        List<(int Index, KeyValuePair<string, Dictionary<string, float[]>> Entry)> createdEntries)
    {
        helper.BulkExecuteNonQuery(
            "INSERT INTO embedding_cache (cache_key, model_key, embedding, idx) VALUES (@cache_key, @model_key, @embedding, @index)",
            createdEntries.SelectMany(element => {
                return element.Entry.Value.Select(model => new object[]
                {
                    new SqliteParameter("@cache_key", element.Entry.Key),
                    new SqliteParameter("@model_key", model.Key),
                    new SqliteParameter("@embedding", SearchdomainHelper.BytesFromFloatArray(model.Value)),
                    new SqliteParameter("@index", element.Index)
                });
            })
        );
    }

    private static async Task UpdateEntryIndicesInStore(
        SQLiteHelper helper,
        List<(int Index, int NewIndex)> changedEntries)
    {
        helper.BulkExecuteNonQuery(
            "UPDATE embedding_cache SET idx = @newIndex WHERE idx = @index",
            changedEntries.Select(element => new object[]
            {
                new SqliteParameter("@index", element.Index),
                new SqliteParameter("@newIndex", -element.NewIndex) // The "-" prevents in-place update collisions
            })
        );
        helper.BulkExecuteNonQuery(
            "UPDATE embedding_cache SET idx = @newIndex WHERE idx = @index",
            changedEntries.Select(element => new object[]
            {
                new SqliteParameter("@index", -element.NewIndex),
                new SqliteParameter("@newIndex", element.NewIndex) // Flip the negative prefix
            })
        );
    }

    private static async Task RemoveEntriesFromStore(
        SQLiteHelper helper,
        List<int> deletedEntries)
    {
        helper.BulkExecuteNonQuery(
            "DELETE FROM embedding_cache WHERE idx = @index",
            deletedEntries.Select(index => new object[]
            {
                new SqliteParameter("@index", index)
            })
        );
    }

    private static async Task AddModelsToIndices(
        SQLiteHelper helper,
        List<(int Index, string Model, string Key, float[] Embedding)> addedModels)
    {
        helper.BulkExecuteNonQuery(
            "INSERT INTO embedding_cache (cache_key, model_key, embedding, idx) VALUES (@cache_key, @model_key, @embedding, @index)",
            addedModels.Select(element => new object[]
            {
                new SqliteParameter("@cache_key", element.Key),
                new SqliteParameter("@model_key", element.Model),
                new SqliteParameter("@embedding", SearchdomainHelper.BytesFromFloatArray(element.Embedding)),
                new SqliteParameter("@index", element.Index)
            })
        );
    }

    private static async Task RemoveModelsFromIndices(
        SQLiteHelper helper,
        List<(int Index, string Model)> removedModels)
    {
        helper.BulkExecuteNonQuery(
            "DELETE FROM embedding_cache WHERE idx = @index AND model_key = @model",
            removedModels.Select(element => new object[]
            {
                new SqliteParameter("@index", element.Index),
                new SqliteParameter("@model", element.Model)
            })
        );
    }


    private static (Dictionary<int, KeyValuePair<string, Dictionary<string, float[]>>> positionToEntry,
        Dictionary<string, int> entryToPosition)
    GetCacheMappings(EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache)
    {
        var positionToEntry = new Dictionary<int, KeyValuePair<string, Dictionary<string, float[]>>>();
        var entryToPosition = new Dictionary<string, int>();
        
        int position = 0;
        
        foreach (var entry in embeddingCache)
        {
            positionToEntry[position] = entry;
            entryToPosition[entry.Key] = position;
            position++;
        }
        
        return (positionToEntry, entryToPosition);
    }
}
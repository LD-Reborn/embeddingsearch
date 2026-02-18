using System.Data;
using System.Data.Common;
using System.Text.Json;
using ElmahCore.Mvc.Logger;
using MySql.Data.MySqlClient;
using Server.Helper;
using Shared;
using Shared.Models;
using AdaptiveExpressions;
using System.Collections.Concurrent;

namespace Server;

public class Searchdomain
{
    private readonly string _connectionString;
    private readonly string _provider;
    public AIProvider aIProvider;
    public string searchdomain;
    public int id;
    public SearchdomainSettings settings;
    public EnumerableLruCache<string, DateTimedSearchResult> queryCache; // Key: query, Value: Search results for that query (with timestamp)
    public ConcurrentDictionary<string, Entity> entityCache;
    public ConcurrentBag<string> modelsInUse;
    public EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache;
    private readonly MySqlConnection connection;
    public SQLHelper helper;
    private readonly ILogger _logger;

    public Searchdomain(string searchdomain, string connectionString, AIProvider aIProvider, EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache, ILogger logger, string provider = "sqlserver", bool runEmpty = false)
    {
        _connectionString = connectionString;
        _provider = provider.ToLower();
        this.searchdomain = searchdomain;
        this.aIProvider = aIProvider;
        this.embeddingCache = embeddingCache;
        this._logger = logger;
        entityCache = [];
        connection = new MySqlConnection(connectionString);
        connection.Open();
        helper = new SQLHelper(connection, connectionString);
        settings = GetSettings();
        queryCache = new(settings.QueryCacheSize);
        modelsInUse = []; // To make the compiler shut up - it is set in UpdateSearchDomain() don't worry // yeah, about that...
        if (!runEmpty)
        {
            id = GetID().Result;
            UpdateEntityCache();
        }
    }

    public void UpdateEntityCache()
    {
        InvalidateSearchCache();
        Dictionary<string, dynamic> parametersIDSearchdomain = new()
        {
            ["id"] = this.id
        };
        DbDataReader embeddingReader = helper.ExecuteSQLCommand("SELECT id, id_datapoint, model, embedding FROM embedding WHERE id_searchdomain = @id", parametersIDSearchdomain);
        Dictionary<int, Dictionary<string, float[]>> embedding_unassigned = [];
        try
        {
            while (embeddingReader.Read())
            {
                int? id_datapoint_debug = null;
                try
                {
                    int id_datapoint = embeddingReader.GetInt32(1);
                    id_datapoint_debug = id_datapoint;
                    string model = embeddingReader.GetString(2);
                    long length = embeddingReader.GetBytes(3, 0, null, 0, 0);
                    byte[] embedding = new byte[length];
                    embeddingReader.GetBytes(3, 0, embedding, 0, (int) length);
                    if (embedding_unassigned.TryGetValue(id_datapoint, out Dictionary<string, float[]>? embedding_unassigned_id_datapoint))
                    {
                        embedding_unassigned[id_datapoint][model] = SearchdomainHelper.FloatArrayFromBytes(embedding);
                    }
                    else
                    {
                        embedding_unassigned[id_datapoint] = new()
                        {
                            [model] = SearchdomainHelper.FloatArrayFromBytes(embedding)
                        };
                    }
                } catch (Exception e)
                {
                    _logger.LogError("Error reading embedding (id: {id_datapoint}) from database: {e.Message} - {e.StackTrace}", [id_datapoint_debug, e.Message, e.StackTrace]);
                    ElmahCore.ElmahExtensions.RaiseError(e);
                }
            }
        } finally
        {
            embeddingReader.Close();
        }

        DbDataReader datapointReader = helper.ExecuteSQLCommand("SELECT d.id, d.id_entity, d.name, d.probmethod_embedding, d.similaritymethod, d.hash FROM datapoint d JOIN entity ent ON d.id_entity = ent.id JOIN searchdomain s ON ent.id_searchdomain = s.id WHERE s.id = @id", parametersIDSearchdomain);
        Dictionary<int, ConcurrentBag<Datapoint>> datapoint_unassigned = [];
        try
        {
            while (datapointReader.Read())
            {
                int id = datapointReader.GetInt32(0);
                int id_entity = datapointReader.GetInt32(1);
                string name = datapointReader.GetString(2);
                string probmethodString = datapointReader.GetString(3);
                string similarityMethodString = datapointReader.GetString(4);
                string hash = datapointReader.GetString(5);
                ProbMethodEnum probmethodEnum = (ProbMethodEnum)Enum.Parse(
                    typeof(ProbMethodEnum),
                    probmethodString
                );
                SimilarityMethodEnum similairtyMethodEnum = (SimilarityMethodEnum)Enum.Parse(
                    typeof(SimilarityMethodEnum),
                    similarityMethodString
                );
                ProbMethod probmethod = new(probmethodEnum);
                SimilarityMethod similarityMethod = new(similairtyMethodEnum);
                if (embedding_unassigned.TryGetValue(id, out Dictionary<string, float[]>? embeddings) && probmethod is not null)
                {
                    embedding_unassigned.Remove(id);
                    if (!datapoint_unassigned.ContainsKey(id_entity))
                    {
                        datapoint_unassigned[id_entity] = [];
                    }
                    datapoint_unassigned[id_entity].Add(new Datapoint(name, probmethod, similarityMethod, hash, [.. embeddings.Select(kv => (kv.Key, kv.Value))], id));
                }
            }
        } finally
        {
            datapointReader.Close();
        }

        DbDataReader attributeReader = helper.ExecuteSQLCommand("SELECT a.id, a.id_entity, a.attribute, a.value FROM attribute a JOIN entity ent ON a.id_entity = ent.id JOIN searchdomain s ON ent.id_searchdomain = s.id WHERE s.id = @id", parametersIDSearchdomain);
        Dictionary<int, Dictionary<string, string>> attributes_unassigned = [];
        try
        {
            while (attributeReader.Read())
            {
                //"SELECT id, id_entity, attribute, value FROM attribute JOIN entity on attribute.id_entity as en JOIN searchdomain on en.id_searchdomain as sd WHERE sd=@id"
                int id = attributeReader.GetInt32(0);
                int id_entity = attributeReader.GetInt32(1);
                string attribute = attributeReader.GetString(2);
                string value = attributeReader.GetString(3);
                if (!attributes_unassigned.ContainsKey(id_entity))
                {
                    attributes_unassigned[id_entity] = [];
                }
                attributes_unassigned[id_entity].Add(attribute, value);
            }
        } finally
        {
            attributeReader.Close();
        }

        entityCache = [];
        DbDataReader entityReader = helper.ExecuteSQLCommand("SELECT entity.id, name, probmethod FROM entity WHERE id_searchdomain=@id", parametersIDSearchdomain);
        try
        {
            while (entityReader.Read())
            {
                //SELECT id, name, probmethod FROM entity WHERE id_searchdomain=@id
                int id = entityReader.GetInt32(0);
                string name = entityReader.GetString(1);
                string probmethodString = entityReader.GetString(2);
                if (!attributes_unassigned.TryGetValue(id, out Dictionary<string, string>? attributes))
                {
                    attributes = [];
                }
                Probmethods.probMethodDelegate? probmethod = Probmethods.GetMethod(probmethodString);
                if (datapoint_unassigned.TryGetValue(id, out ConcurrentBag<Datapoint>? datapoints) && probmethod is not null)
                {
                    Entity entity = new(attributes, probmethod, probmethodString, datapoints, name)
                    {
                        id = id
                    };
                    entityCache[name] = entity;
                }
            }
        } finally
        {
            entityReader.Close();
        }
        modelsInUse = GetModels(entityCache);
    }

    public List<(float, string)> Search(string query, int? topN = null)
    {
        if (queryCache.TryGetValue(query, out DateTimedSearchResult cachedResult))
        {
            cachedResult.AccessDateTimes.Add(DateTime.Now);
            return [.. cachedResult.Results.Select(r => (r.Score, r.Name))];
        }

        Dictionary<string, float[]> queryEmbeddings = GetQueryEmbeddings(query);

        List<(float, string)> result = [];
        foreach ((string name, Entity entity) in entityCache)
        {
            result.Add((EvaluateEntityAgainstQueryEmbeddings(entity, queryEmbeddings), entity.name));
        }
        IEnumerable<(float, string)> sortedResults = result.OrderByDescending(s => s.Item1);
        if (topN is not null)
        {
            sortedResults = sortedResults.Take(topN ?? 0);
        }
        List<(float, string)> results = [.. sortedResults];
        List<ResultItem> searchResult = new(
            [.. sortedResults.Select(r =>
                new ResultItem(r.Item1, r.Item2 ))]
        );
        queryCache.Set(query, new DateTimedSearchResult(DateTime.Now, searchResult));
        return results;
    }

    public Dictionary<string, float[]> GetQueryEmbeddings(string query)
    {
        bool hasQuery = embeddingCache.TryGetValue(query, out Dictionary<string, float[]>? queryEmbeddings);
        bool allModelsInQuery = queryEmbeddings is not null && modelsInUse.All(model => queryEmbeddings.ContainsKey(model));
        if (!(hasQuery && allModelsInQuery) || queryEmbeddings is null)
        {
            queryEmbeddings = Datapoint.GetEmbeddings(query, modelsInUse, aIProvider, embeddingCache);
            if (!embeddingCache.TryGetValue(query, out var embeddingCacheForCurrentQuery))
            {
                embeddingCache.Set(query, queryEmbeddings);
            }
            else // embeddingCache already has an entry for this query, so the missing model-embedding pairs have to be filled in
            {
                foreach (KeyValuePair<string, float[]> kvp in queryEmbeddings) // kvp.Key = model, kvp.Value = embedding
                {
                    if (!embeddingCache.TryGetValue(kvp.Key, out var _))
                    {
                        embeddingCacheForCurrentQuery[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        return queryEmbeddings;
    }

    public void UpdateModelsInUse()
    {
        modelsInUse = GetModels(entityCache);
    }

    private static float EvaluateEntityAgainstQueryEmbeddings(Entity entity, Dictionary<string, float[]> queryEmbeddings)
    {
        List<(string, float)> datapointProbs = [];
        foreach (Datapoint datapoint in entity.datapoints)
        {
            SimilarityMethod similarityMethod = datapoint.similarityMethod;
            List<(string, float)> list = [];
            foreach ((string, float[]) embedding in datapoint.embeddings)
            {
                string key = embedding.Item1;
                float value = similarityMethod.method(queryEmbeddings[embedding.Item1], embedding.Item2);
                list.Add((key, value));
            }
            datapointProbs.Add((datapoint.name, datapoint.probMethod.method(list)));
        }
        return entity.probMethod(datapointProbs);
    }

    public static ConcurrentBag<string> GetModels(ConcurrentDictionary<string, Entity> entities)
    {
        ConcurrentBag<string> result = [];
        foreach (KeyValuePair<string, Entity> element in entities)
        {
            Entity entity = element.Value;
            lock (entity)
            {
                foreach (Datapoint datapoint in entity.datapoints)
                {
                    foreach ((string, float[]) tuple in datapoint.embeddings)
                    {
                        string model = tuple.Item1;
                        if (!result.Contains(model))
                        {
                            result.Add(model);
                        }
                    }
                }
            }
        }
        return result;
    }

    public async Task<int> GetID()
    {
        Dictionary<string, object?> parameters = new()
        {
            { "name", this.searchdomain }
        };
        return (await helper.ExecuteQueryAsync("SELECT id from searchdomain WHERE name = @name", parameters, x => x.GetInt32(0))).First();
    }

    public SearchdomainSettings GetSettings()
    {
        return DatabaseHelper.GetSearchdomainSettings(helper, searchdomain);
    }

    public void ReconciliateOrInvalidateCacheForNewOrUpdatedEntity(Entity entity)
    {
        if (settings.CacheReconciliation)
        {
            foreach (var element in queryCache)
            {
                string query = element.Key;
                DateTimedSearchResult searchResult = element.Value;

                Dictionary<string, float[]> queryEmbeddings = GetQueryEmbeddings(query);
                float evaluationResult = EvaluateEntityAgainstQueryEmbeddings(entity, queryEmbeddings);

                searchResult.Results.RemoveAll(x => x.Name == entity.name); // If entity already exists in that results list: remove it.

                ResultItem newItem = new(evaluationResult, entity.name);
                int index = searchResult.Results.BinarySearch(
                    newItem,
                    Comparer<ResultItem>.Create((a, b) => b.Score.CompareTo(a.Score)) // Invert searching order
                );
                if (index < 0) // If not found, BinarySearch gives the bitwise complement
                    index = ~index;
                searchResult.Results.Insert(index, newItem);
            }
        }
        else
        {
            InvalidateSearchCache();
        }
    }

    public void ReconciliateOrInvalidateCacheForDeletedEntity(Entity entity)
    {
        if (settings.CacheReconciliation)
        {
            foreach (KeyValuePair<string, DateTimedSearchResult> element in queryCache)
            {
                string query = element.Key;
                DateTimedSearchResult searchResult = element.Value;
                searchResult.Results.RemoveAll(x => x.Name == entity.name);
            }
        }
        else
        {
            InvalidateSearchCache();
        }
    }

    public void InvalidateSearchCache()
    {
        queryCache = new(settings.QueryCacheSize);
    }

    public long GetSearchCacheSize()
    {
        long EmbeddingCacheUtilization = 0;
        foreach (var entry in queryCache)
        {
            EmbeddingCacheUtilization += sizeof(int); // string length prefix
            EmbeddingCacheUtilization += entry.Key.Length * sizeof(char); // string characters
            EmbeddingCacheUtilization += entry.Value.EstimateSize();
        }
        return EmbeddingCacheUtilization;
    }
}

using System.Data;
using System.Data.Common;
using System.Text.Json;
using ElmahCore.Mvc.Logger;
using MySql.Data.MySqlClient;
using Server.Helper;
using Shared.Models;
using AdaptiveExpressions;

namespace Server;

public class Searchdomain
{
    private readonly string _connectionString;
    private readonly string _provider;
    public AIProvider aIProvider;
    public string searchdomain;
    public int id;
    public SearchdomainSettings settings;
    public Dictionary<string, DateTimedSearchResult> searchCache; // Key: query, Value: Search results for that query (with timestamp)
    public List<Entity> entityCache;
    public List<string> modelsInUse;
    public LRUCache<string, Dictionary<string, float[]>> embeddingCache;
    private readonly MySqlConnection connection;
    public SQLHelper helper;
    private readonly ILogger _logger;

    public Searchdomain(string searchdomain, string connectionString, AIProvider aIProvider, LRUCache<string, Dictionary<string, float[]>> embeddingCache, ILogger logger, string provider = "sqlserver", bool runEmpty = false)
    {
        _connectionString = connectionString;
        _provider = provider.ToLower();
        this.searchdomain = searchdomain;
        this.aIProvider = aIProvider;
        this.embeddingCache = embeddingCache;
        this._logger = logger;
        searchCache = [];
        entityCache = [];
        connection = new MySqlConnection(connectionString);
        connection.Open();
        helper = new SQLHelper(connection, connectionString);
        settings = GetSettings();
        modelsInUse = []; // To make the compiler shut up - it is set in UpdateSearchDomain() don't worry // yeah, about that...
        if (!runEmpty)
        {
            GetID();
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
        DbDataReader embeddingReader = helper.ExecuteSQLCommand("SELECT e.id, e.id_datapoint, e.model, e.embedding FROM embedding e JOIN datapoint d ON e.id_datapoint = d.id JOIN entity ent ON d.id_entity = ent.id JOIN searchdomain s ON ent.id_searchdomain = s.id WHERE s.id = @id", parametersIDSearchdomain);
        Dictionary<int, Dictionary<string, float[]>> embedding_unassigned = [];
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
        embeddingReader.Close();

        DbDataReader datapointReader = helper.ExecuteSQLCommand("SELECT d.id, d.id_entity, d.name, d.probmethod_embedding, d.similaritymethod, d.hash FROM datapoint d JOIN entity ent ON d.id_entity = ent.id JOIN searchdomain s ON ent.id_searchdomain = s.id WHERE s.id = @id", parametersIDSearchdomain);
        Dictionary<int, List<Datapoint>> datapoint_unassigned = [];
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
            ProbMethod probmethod = new(probmethodEnum, _logger);
            SimilarityMethod similarityMethod = new(similairtyMethodEnum, _logger);
            if (embedding_unassigned.TryGetValue(id, out Dictionary<string, float[]>? embeddings) && probmethod is not null)
            {
                embedding_unassigned.Remove(id);
                if (!datapoint_unassigned.ContainsKey(id_entity))
                {
                    datapoint_unassigned[id_entity] = [];
                }
                datapoint_unassigned[id_entity].Add(new Datapoint(name, probmethod, similarityMethod, hash, [.. embeddings.Select(kv => (kv.Key, kv.Value))]));
            }
        }
        datapointReader.Close();

        DbDataReader attributeReader = helper.ExecuteSQLCommand("SELECT a.id, a.id_entity, a.attribute, a.value FROM attribute a JOIN entity ent ON a.id_entity = ent.id JOIN searchdomain s ON ent.id_searchdomain = s.id WHERE s.id = @id", parametersIDSearchdomain);
        Dictionary<int, Dictionary<string, string>> attributes_unassigned = [];
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
        attributeReader.Close();

        entityCache = [];
        DbDataReader entityReader = helper.ExecuteSQLCommand("SELECT entity.id, name, probmethod FROM entity WHERE id_searchdomain=@id", parametersIDSearchdomain);
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
            if (datapoint_unassigned.TryGetValue(id, out List<Datapoint>? datapoints) && probmethod is not null)
            {
                Entity entity = new(attributes, probmethod, probmethodString, datapoints, name)
                {
                    id = id
                };
                entityCache.Add(entity);
            }
        }
        entityReader.Close();
        modelsInUse = GetModels(entityCache);
    }

    public List<(float, string)> Search(string query, int? topN = null)
    {
        if (searchCache.TryGetValue(query, out DateTimedSearchResult cachedResult))
        {
            cachedResult.AccessDateTimes.Add(DateTime.Now);
            return [.. cachedResult.Results.Select(r => (r.Score, r.Name))];
        }

        bool hasQuery = embeddingCache.TryGet(query, out Dictionary<string, float[]>? queryEmbeddings);
        bool allModelsInQuery = queryEmbeddings is not null && modelsInUse.All(model => queryEmbeddings.ContainsKey(model));
        if (!(hasQuery && allModelsInQuery))
        {
            queryEmbeddings = Datapoint.GenerateEmbeddings(query, modelsInUse, aIProvider, embeddingCache);
            if (!embeddingCache.TryGet(query, out var embeddingCacheForCurrentQuery))
            {
                embeddingCache.Set(query, queryEmbeddings);
            }
            else // embeddingCache already has an entry for this query, so the missing model-embedding pairs have to be filled in
            {
                foreach (KeyValuePair<string, float[]> kvp in queryEmbeddings) // kvp.Key = model, kvp.Value = embedding
                {
                    if (!embeddingCache.TryGet(kvp.Key, out var _))
                    {
                        embeddingCacheForCurrentQuery[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        List<(float, string)> result = [];

        foreach (Entity entity in entityCache)
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
            result.Add((entity.probMethod(datapointProbs), entity.name));
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
        searchCache[query] = new DateTimedSearchResult(DateTime.Now, searchResult);
        return results;
    }

    public static List<string> GetModels(List<Entity> entities)
    {
        List<string> result = [];
        foreach (Entity entity in entities)
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
        return result;
    }

    public int GetID()
    {
        Dictionary<string, dynamic> parameters = new()
        {
            ["name"] = this.searchdomain
        };
        DbDataReader reader = helper.ExecuteSQLCommand("SELECT id from searchdomain WHERE name = @name", parameters);
        reader.Read();
        this.id = reader.GetInt32(0);
        reader.Close();
        return this.id;
    }

    public SearchdomainSettings GetSettings()
    {
        Dictionary<string, dynamic> parameters = new()
        {
            ["name"] = searchdomain
        };
        DbDataReader reader = helper.ExecuteSQLCommand("SELECT settings from searchdomain WHERE name = @name", parameters);
        reader.Read();
        string settingsString = reader.GetString(0);
        reader.Close();
        return JsonSerializer.Deserialize<SearchdomainSettings>(settingsString);
    }

    public void InvalidateSearchCache()
    {
        searchCache = [];
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Server.Exceptions;
using Shared.Models;

namespace Server.Helper;

public class SearchdomainHelper(ILogger<SearchdomainHelper> logger, DatabaseHelper databaseHelper)
{
    private readonly ILogger<SearchdomainHelper> _logger = logger;
    private readonly DatabaseHelper _databaseHelper = databaseHelper;

    public static byte[] BytesFromFloatArray(float[] floats)
    {
        var byteArray = new byte[floats.Length * 4];
        var floatArray = floats.ToArray();
        Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);
        return byteArray;
    }

    public static float[] FloatArrayFromBytes(byte[] bytes)
    {
        var floatArray = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floatArray, 0, bytes.Length);
        return floatArray;
    }

    public static bool CacheHasEntity(List<Entity> entityCache, string name)
    {
        return CacheGetEntity(entityCache, name) is not null;
    }

    public static Entity? CacheGetEntity(List<Entity> entityCache, string name)
    {
        foreach (Entity entity in entityCache)
        {
            if (entity.name == name)
            {
                return entity;
            }
        }
        return null;
    }

    public List<Entity>? EntitiesFromJSON(List<Entity> entityCache, Dictionary<string, Dictionary<string, float[]>> embeddingCache, AIProvider aIProvider, SQLHelper helper, ILogger logger, string json)
    {
        List<JSONEntity>? jsonEntities = JsonSerializer.Deserialize<List<JSONEntity>>(json);
        if (jsonEntities is null)
        {
            return null;
        }

        // toBeCached: model -> [datapoint.text * n]
        Dictionary<string, List<string>> toBeCached = [];
        foreach (JSONEntity jSONEntity in jsonEntities)
        {
            foreach (JSONDatapoint datapoint in jSONEntity.Datapoints)
            {
                foreach (string model in datapoint.Model)
                {
                    if (!toBeCached.ContainsKey(model))
                    {
                        toBeCached[model] = [];
                    }
                    toBeCached[model].Add(datapoint.Text);
                }
            }
        }
        ConcurrentQueue<Entity> retVal = [];
        ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = 16 }; // <-- This is needed! Otherwise if we try to index 100+ entities at once, it spawns 100 threads, exploding the SQL pool
        Parallel.ForEach(jsonEntities, parallelOptions, jSONEntity =>
        {
            using var tempHelper = helper.DuplicateConnection();
            var entity = EntityFromJSON(entityCache, embeddingCache, aIProvider, tempHelper, logger, jSONEntity);
            if (entity is not null)
            {
                retVal.Enqueue(entity);
            }
        });
        return [.. retVal];
    }

    public Entity? EntityFromJSON(List<Entity> entityCache, Dictionary<string, Dictionary<string, float[]>> embeddingCache, AIProvider aIProvider, SQLHelper helper, ILogger logger, JSONEntity jsonEntity) //string json)
    {
        Dictionary<string, Dictionary<string, float[]>> embeddingsLUT = []; // embeddingsLUT: hash -> model -> [embeddingValues * n]
        int? preexistingEntityID = _databaseHelper.GetEntityID(helper, jsonEntity.Name, jsonEntity.Searchdomain);
        if (preexistingEntityID is not null)
        {
            lock (helper.connection) // TODO change this to helper and do A/B tests (i.e. before/after)
            {
                Dictionary<string, dynamic> parameters = new()
                {
                    { "id", preexistingEntityID }
                };
                System.Data.Common.DbDataReader reader = helper.ExecuteSQLCommand("SELECT e.model, e.embedding, d.hash FROM datapoint d JOIN embedding e ON d.id = e.id_datapoint WHERE d.id_entity = @id", parameters);
                while (reader.Read())
                {
                    string model = reader.GetString(0);
                    long length = reader.GetBytes(1, 0, null, 0, 0);
                    byte[] embeddingBytes = new byte[length];
                    reader.GetBytes(1, 0, embeddingBytes, 0, (int)length);
                    float[] embeddingValues = FloatArrayFromBytes(embeddingBytes);
                    string hash = reader.GetString(2);
                    if (!embeddingsLUT.ContainsKey(hash))
                    {
                        embeddingsLUT[hash] = [];
                    }
                    embeddingsLUT[hash].TryAdd(model, embeddingValues);
                }
                reader.Close();
            }
            _databaseHelper.RemoveEntity(entityCache, helper, jsonEntity.Name, jsonEntity.Searchdomain); // TODO only remove entity if there is actually a change somewhere. Perhaps create 3 datapoint lists to operate with: 1. delete, 2. update, 3. create
        }
        int id_entity = DatabaseHelper.DatabaseInsertEntity(helper, jsonEntity.Name, jsonEntity.Probmethod, _databaseHelper.GetSearchdomainID(helper, jsonEntity.Searchdomain));
        foreach (KeyValuePair<string, string> attribute in jsonEntity.Attributes)
        {
            DatabaseHelper.DatabaseInsertAttribute(helper, attribute.Key, attribute.Value, id_entity); // TODO implement bulk insert to reduce number of queries
        }

        List<Datapoint> datapoints = [];
        foreach (JSONDatapoint jsonDatapoint in jsonEntity.Datapoints)
        {
            string hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jsonDatapoint.Text)));
            Dictionary<string, float[]> embeddings = [];
            if (embeddingsLUT.ContainsKey(hash))
            {
                Dictionary<string, float[]> hashLUT = embeddingsLUT[hash];
                foreach (string model in jsonDatapoint.Model)
                {
                    if (hashLUT.ContainsKey(model))
                    {
                        embeddings.Add(model, hashLUT[model]);
                    }
                    else
                    {
                        var additionalEmbeddings = Datapoint.GenerateEmbeddings(jsonDatapoint.Text, [model], aIProvider, embeddingCache);
                        embeddings.Add(model, additionalEmbeddings.First().Value);
                    }
                }
            }
            else
            {
                embeddings = Datapoint.GenerateEmbeddings(jsonDatapoint.Text, [.. jsonDatapoint.Model], aIProvider, embeddingCache);
            }
            var probMethod_embedding = new ProbMethod(jsonDatapoint.Probmethod_embedding, logger) ?? throw new ProbMethodNotFoundException(jsonDatapoint.Probmethod_embedding);
            var similarityMethod = new SimilarityMethod(jsonDatapoint.SimilarityMethod, logger) ?? throw new SimilarityMethodNotFoundException(jsonDatapoint.SimilarityMethod);
            Datapoint datapoint = new(jsonDatapoint.Name, probMethod_embedding, similarityMethod, hash, [.. embeddings.Select(kv => (kv.Key, kv.Value))]);
            int id_datapoint = DatabaseHelper.DatabaseInsertDatapoint(helper, jsonDatapoint.Name, jsonDatapoint.Probmethod_embedding, jsonDatapoint.SimilarityMethod, hash, id_entity); // TODO make this a bulk add action to reduce number of queries
            List<(string model, byte[] embedding)> data = [];
            foreach ((string, float[]) embedding in datapoint.embeddings)
            {
                data.Add((embedding.Item1, BytesFromFloatArray(embedding.Item2)));
            }
            DatabaseHelper.DatabaseInsertEmbeddingBulk(helper, id_datapoint, data);
            datapoints.Add(datapoint);
        }

        var probMethod = Probmethods.GetMethod(jsonEntity.Probmethod) ?? throw new Exception($"Unknown probmethod name {jsonEntity.Probmethod}");
        Entity entity = new(jsonEntity.Attributes, probMethod, datapoints, jsonEntity.Name)
        {
            id = id_entity
        };
        entityCache.Add(entity);
        return entity;
    }
}
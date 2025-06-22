using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySql.Data.MySqlClient;
using OllamaSharp;

namespace Server;

public static class SearchdomainHelper
{
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
    
    public static List<Entity>? EntitiesFromJSON(List<Entity> entityCache, Dictionary<string, Dictionary<string, float[]>> embeddingCache, OllamaApiClient ollama, SQLHelper helper, string json)
    {
        List<JSONEntity>? jsonEntities = JsonSerializer.Deserialize<List<JSONEntity>>(json);
        if (jsonEntities is null)
        {
            return null;
        }

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
        Parallel.ForEach(jsonEntities, jSONEntity =>
        {
            var tempHelper = helper.DuplicateConnection();
            var entity = EntityFromJSON(entityCache, embeddingCache, ollama, tempHelper, jSONEntity);
            if (entity is not null)
            {
                retVal.Enqueue(entity);
            }
        });
        return [.. retVal];
    }
    
    public static Entity? EntityFromJSON(List<Entity> entityCache, Dictionary<string, Dictionary<string, float[]>> embeddingCache, OllamaApiClient ollama, SQLHelper helper, JSONEntity jsonEntity) //string json)
    {
        Dictionary<string, Dictionary<string, float[]>> embeddingsLUT = [];
        int? preexistingEntityID = DatabaseHelper.GetEntityID(helper, jsonEntity.Name, jsonEntity.Searchdomain);
        if (preexistingEntityID is not null)
        {
            lock (helper.connection)
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
            DatabaseHelper.RemoveEntity(entityCache, helper, jsonEntity.Name, jsonEntity.Searchdomain); // TODO only remove entity if there is actually a change somewhere. Perhaps create 3 datapoint lists to operate with: 1. delete, 2. update, 3. create
        }
        int id_entity = DatabaseHelper.DatabaseInsertEntity(helper, jsonEntity.Name, jsonEntity.Probmethod, DatabaseHelper.GetSearchdomainID(helper, jsonEntity.Searchdomain));
        foreach (KeyValuePair<string, string> attribute in jsonEntity.Attributes)
        {
            DatabaseHelper.DatabaseInsertAttribute(helper, attribute.Key, attribute.Value, id_entity); // TODO implement bulk insert to reduce number of queries
        }

        List<Datapoint> datapoints = [];
        foreach (JSONDatapoint jsonDatapoint in jsonEntity.Datapoints)
        {
            string hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jsonDatapoint.Text)));
            Dictionary<string, float[]> embeddings = embeddingsLUT.ContainsKey(hash) ? embeddingsLUT[hash] : [];
            if (embeddings.Count == 0)
            {
                embeddings = Datapoint.GenerateEmbeddings(jsonDatapoint.Text, [.. jsonDatapoint.Model], ollama, embeddingCache);
            }
            var probMethod_embedding = Probmethods.GetMethod(jsonDatapoint.Probmethod_embedding) ?? throw new Exception($"Unknown probmethod name {jsonDatapoint.Probmethod_embedding}");
            Datapoint datapoint = new(jsonDatapoint.Name, probMethod_embedding, hash, [.. embeddings.Select(kv => (kv.Key, kv.Value))]);
            int id_datapoint = DatabaseHelper.DatabaseInsertDatapoint(helper, jsonDatapoint.Name, jsonDatapoint.Probmethod_embedding, hash, id_entity); // TODO make this a bulk add action to reduce number of queries
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
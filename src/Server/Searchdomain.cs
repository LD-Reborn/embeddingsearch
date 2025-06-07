using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
//using System.Data.SqlClient;
//using Microsoft.Data.SqlClient;
using MySql.Data;
using MySql.Data.MySqlClient;
using System.Threading.Tasks;
using Npgsql;
using System.Collections.Generic;
using OllamaSharp;
using OllamaSharp.Models;
using System.Configuration;
using System.Data.SqlClient;
using Mysqlx.Resultset;
using System.Collections.Immutable;
using System.Text.Json;
using System.Numerics.Tensors;
using Server;

namespace Server;

public class Searchdomain
{
    private readonly string _connectionString;
    private readonly string _provider;
    public OllamaApiClient ollama;
    public Probmethods probmethods;
    public string searchdomain;
    public int id;
    public Dictionary<string, List<(DateTime, List<(float, string)>)>> searchCache; // Yeah look at this abomination. searchCache[x][0] = last accessed time, searchCache[x][1] = results for x
    public List<Entity> entityCache;
    public List<string> modelsInUse;
    public Dictionary<string, Dictionary<string, float[]>> embeddingCache;
    public int embeddingCacheMaxSize = 10000000;
    private readonly MySqlConnection connection;
    public SQLHelper helper;

    // TODO Add settings and update cli/program.cs, as well as DatabaseInsertSearchdomain()

    public Searchdomain(string searchdomain, string connectionString, OllamaApiClient ollama, string provider = "sqlserver", bool runEmpty = false)
    {
        _connectionString = connectionString;
        _provider = provider.ToLower();
        this.searchdomain = searchdomain;
        this.ollama = ollama;
        searchCache = [];
        entityCache = [];
        embeddingCache = [];
        connection = new MySqlConnection(connectionString);
        connection.Open();
        helper = new SQLHelper(connection);
        probmethods = new();
        modelsInUse = []; // To make the compiler shut up - it is set in UpdateSearchDomain() don't worry // yeah, about that...
        if (!runEmpty)
        {
            GetID();
            UpdateSearchDomain();
        }
    }

    public void UpdateSearchDomain()
    {
        Dictionary<string, dynamic> parametersIDSearchdomain = new()
        {
            ["id"] = this.id
        };
        DbDataReader embeddingReader = helper.ExecuteSQLCommand("SELECT embedding.id, id_datapoint, model, embedding FROM embedding", parametersIDSearchdomain);
        Dictionary<int, Dictionary<string, float[]>> embedding_unassigned = [];
        while (embeddingReader.Read())
        {
            int id_datapoint = embeddingReader.GetInt32(1);
            string model = embeddingReader.GetString(2);
            long length = embeddingReader.GetBytes(3, 0, null, 0, 0);
            byte[] embedding = new byte[length];
            embeddingReader.GetBytes(3, 0, embedding, 0, (int) length);
            if (embedding_unassigned.TryGetValue(id_datapoint, out Dictionary<string, float[]>? embedding_unassigned_id_datapoint))
            {
                embedding_unassigned[id_datapoint][model] = FloatArrayFromBytes(embedding);
            }
            else
            {
                embedding_unassigned[id_datapoint] = new()
                {
                    [model] = FloatArrayFromBytes(embedding)
                };
            }
        }
        embeddingReader.Close();
        
        DbDataReader datapointReader = helper.ExecuteSQLCommand("SELECT id, id_entity, name, probmethod_embedding FROM datapoint", parametersIDSearchdomain);
        Dictionary<int, List<Datapoint>> datapoint_unassigned = [];
        while (datapointReader.Read())
        {
            int id = datapointReader.GetInt32(0);
            int id_entity = datapointReader.GetInt32(1);
            string name = datapointReader.GetString(2);
            string probmethodString = datapointReader.GetString(3);
            Probmethods.probMethodDelegate? probmethod = probmethods.GetMethod(probmethodString);
            if (embedding_unassigned.TryGetValue(id, out Dictionary<string, float[]>? embeddings) && probmethod is not null)
            {
                embedding_unassigned.Remove(id);
                if (!datapoint_unassigned.ContainsKey(id_entity))
                {
                    datapoint_unassigned[id_entity] = [];
                }
                datapoint_unassigned[id_entity].Add(new Datapoint(name, probmethod, [.. embeddings.Select(kv => (kv.Key, kv.Value))]));
            }
        }
        datapointReader.Close();

        DbDataReader attributeReader = helper.ExecuteSQLCommand("SELECT id, id_entity, attribute, value FROM attribute", parametersIDSearchdomain);        
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
            Probmethods.probMethodDelegate? probmethod = probmethods.GetMethod(probmethodString);
            if (datapoint_unassigned.TryGetValue(id, out List<Datapoint>? datapoints) && probmethod is not null)
            {
                Entity entity = new(attributes, probmethod, datapoints, name)
                {
                    id = id
                };
                entityCache.Add(entity);
            }
        }
        entityReader.Close();
        modelsInUse = GetModels(entityCache);
    }

    public List<(float, string)> Search(string query, bool sort=true)
    {
        if (!embeddingCache.TryGetValue(query, out Dictionary<string, float[]>? queryEmbeddings))
        {
            queryEmbeddings = Datapoint.GenerateEmbeddings(query, modelsInUse, ollama);
            if (embeddingCache.Count < embeddingCacheMaxSize) // TODO add better way of managing cache limit hits
            { // Idea: Add access count to each entry. On limit hit, sort the entries by access count and remove the bottom 10% of entries
                embeddingCache.Add(query, queryEmbeddings);
            }
        }

        List<(float, string)> result = [];

        foreach (Entity entity in entityCache)
        {
            List<(string, float)> datapointProbs = [];
            foreach (Datapoint datapoint in entity.datapoints)
            {
                List<(string, float)> list = [];
                foreach ((string, float[]) embedding in datapoint.embeddings)
                {
                    string key = embedding.Item1;
                    float value = Probmethods.Similarity(queryEmbeddings[embedding.Item1], embedding.Item2);
                    list.Add((key, value));
                }
                datapointProbs.Add((datapoint.name, datapoint.probMethod(list)));
            }
            result.Add((entity.probMethod(datapointProbs), entity.name));
        }

        return [.. result.OrderByDescending(s => s.Item1)]; // [.. element] = element.ToList()
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

    public static float[] FloatArrayFromBytes(byte[] bytes)
    {
        var floatArray = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floatArray, 0, bytes.Length);
        return floatArray;
    }

    public static byte[] BytesFromFloatArray(float[] floats)
    {
        var byteArray = new byte[floats.Length * 4];
        var floatArray = floats.ToArray();
        Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);
        return byteArray;
    }

    public Entity? GetEntity(string name)
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

    public bool HasEntity(string name)
    {
        return GetEntity(name) is not null;
    }

    public Entity? EntityFromJSON(string json)
    {
        JSONEntity? jsonEntity = JsonSerializer.Deserialize<JSONEntity>(json);
        if (jsonEntity is null)
        {
            return null;
        }
        if (HasEntity(jsonEntity.Name))
        {
            RemoveEntity(jsonEntity.Name);

        }
        int id_entity = DatabaseInsertEntity(jsonEntity.Name, jsonEntity.Probmethod, id);
        foreach (KeyValuePair<string, string> attribute in jsonEntity.Attributes)
        {
            DatabaseInsertAttribute(attribute.Key, attribute.Value, id_entity);
        }

        List<Datapoint> datapoints = [];
        
        foreach (JSONDatapoint jsonDatapoint in jsonEntity.Datapoints)
        {
            Dictionary<string, float[]> embeddings = Datapoint.GenerateEmbeddings(jsonDatapoint.Text, [.. jsonDatapoint.Model], ollama, embeddingCache);
            var probMethod_embedding = probmethods.GetMethod(jsonDatapoint.Probmethod_embedding) ?? throw new Exception($"Unknown probmethod name {jsonDatapoint.Probmethod_embedding}");
            Datapoint datapoint = new(jsonDatapoint.Name, probMethod_embedding, [.. embeddings.Select(kv => (kv.Key, kv.Value))]);
            int id_datapoint = DatabaseInsertDatapoint(jsonDatapoint.Name, jsonDatapoint.Probmethod_embedding, id_entity);
            foreach ((string, float[]) embedding in datapoint.embeddings)
            {
                DatabaseInsertEmbedding(id_datapoint, embedding.Item1, BytesFromFloatArray(embedding.Item2));
            }
            datapoints.Add(datapoint);
        }

        var probMethod = probmethods.GetMethod(jsonEntity.Probmethod) ?? throw new Exception($"Unknown probmethod name {jsonEntity.Probmethod}");
        Entity entity = new(jsonEntity.Attributes, probMethod, datapoints, jsonEntity.Name)
        {
            id = id_entity
        };
        entityCache.Add(entity);
        return entity;
    }

    public List<Entity>? EntitiesFromJSON(string json)
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
        Dictionary<string, Dictionary<string, float[]>> cache = []; // local cache
        foreach (KeyValuePair<string, List<string>> cacheThis in toBeCached)
        {
            string model = cacheThis.Key;
            List<string> contents = cacheThis.Value;
            if (contents.Count == 0)
            {
                cache[model] = [];
                continue;
            }
            cache[model] = Datapoint.GenerateEmbeddings(contents, model, ollama, embeddingCache);
        }
        var tempEmbeddingCache = embeddingCache;
        embeddingCache = cache;
        List<Entity> retVal = [];
        foreach (JSONEntity jSONEntity in jsonEntities)
        {
            retVal.Append(EntityFromJSON(JsonSerializer.Serialize(jSONEntity)));
        }
        embeddingCache = tempEmbeddingCache;
        return retVal;
    }

    public void RemoveEntity(string name)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name }
        };
        helper.ExecuteSQLNonQuery("DELETE embedding.* FROM embedding JOIN datapoint dp ON id_datapoint = dp.id JOIN entity ON id_entity = entity.id WHERE entity.name = @name", parameters);
        helper.ExecuteSQLNonQuery("DELETE datapoint.* FROM datapoint JOIN entity ON id_entity = entity.id WHERE entity.name = @name", parameters);
        helper.ExecuteSQLNonQuery("DELETE attribute.* FROM attribute JOIN entity ON id_entity = entity.id WHERE entity.name = @name", parameters);
        helper.ExecuteSQLNonQuery("DELETE FROM entity WHERE name = @name", parameters);
        entityCache.RemoveAll(entity => entity.name == name);
    }

    public int DatabaseInsertSearchdomain(string name)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "settings", "{}"} // TODO add settings. It's not used yet, but maybe it's needed someday...
        };
        return helper.ExecuteSQLCommandGetInsertedID("INSERT INTO searchdomain (name, settings) VALUES (@name, @settings)", parameters);
    }

    public int DatabaseInsertEntity(string name, string probmethod, int id_searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "probmethod", probmethod },
            { "id_searchdomain", id_searchdomain }
        };
        return helper.ExecuteSQLCommandGetInsertedID("INSERT INTO entity (name, probmethod, id_searchdomain) VALUES (@name, @probmethod, @id_searchdomain)", parameters);
    }

    public int DatabaseInsertAttribute(string attribute, string value, int id_entity)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "attribute", attribute },
            { "value", value },
            { "id_entity", id_entity }
        };
        return helper.ExecuteSQLCommandGetInsertedID("INSERT INTO attribute (attribute, value, id_entity) VALUES (@attribute, @value, @id_entity)", parameters);
    }


    public int DatabaseInsertDatapoint(string name, string probmethod_embedding, int id_entity)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "probmethod_embedding", probmethod_embedding },
            { "id_entity", id_entity }
        };
        return helper.ExecuteSQLCommandGetInsertedID("INSERT INTO datapoint (name, probmethod_embedding, id_entity) VALUES (@name, @probmethod_embedding, @id_entity)", parameters);
    }

    public int DatabaseInsertEmbedding(int id_datapoint, string model, byte[] embedding)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "id_datapoint", id_datapoint },
            { "model", model },
            { "embedding", embedding }
        };
        return helper.ExecuteSQLCommandGetInsertedID("INSERT INTO embedding (id_datapoint, model, embedding) VALUES (@id_datapoint, @model, @embedding)", parameters);
    }
}

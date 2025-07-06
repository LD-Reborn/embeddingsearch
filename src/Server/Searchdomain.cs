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
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using server;

namespace Server;

public class Searchdomain
{
    private readonly string _connectionString;
    private readonly string _provider;
    public AIProvider aIProvider;
    public string searchdomain;
    public int id;
    public Dictionary<string, List<(DateTime, List<(float, string)>)>> searchCache; // Yeah look at this abomination. searchCache[x][0] = last accessed time, searchCache[x][1] = results for x
    public List<Entity> entityCache;
    public List<string> modelsInUse;
    public Dictionary<string, Dictionary<string, float[]>> embeddingCache;
    public int embeddingCacheMaxSize = 10000000;
    private readonly MySqlConnection connection;
    public SQLHelper helper;
    private readonly ILogger _logger;

    // TODO Add settings and update cli/program.cs, as well as DatabaseInsertSearchdomain()

    public Searchdomain(string searchdomain, string connectionString, AIProvider aIProvider, Dictionary<string, Dictionary<string, float[]>> embeddingCache, ILogger logger, string provider = "sqlserver", bool runEmpty = false)
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
        modelsInUse = []; // To make the compiler shut up - it is set in UpdateSearchDomain() don't worry // yeah, about that...
        if (!runEmpty)
        {
            GetID();
            UpdateEntityCache();
        }
    }

    public void UpdateEntityCache()
    {
        Dictionary<string, dynamic> parametersIDSearchdomain = new()
        {
            ["id"] = this.id
        };
        DbDataReader embeddingReader = helper.ExecuteSQLCommand("SELECT embedding.id, id_datapoint, model, embedding FROM embedding", parametersIDSearchdomain); // TODO fix: parametersIDSearchdomain defined, but not used
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
                embedding_unassigned[id_datapoint][model] = SearchdomainHelper.FloatArrayFromBytes(embedding);
            }
            else
            {
                embedding_unassigned[id_datapoint] = new()
                {
                    [model] = SearchdomainHelper.FloatArrayFromBytes(embedding)
                };
            }
        }
        embeddingReader.Close();

        DbDataReader datapointReader = helper.ExecuteSQLCommand("SELECT id, id_entity, name, probmethod_embedding, hash FROM datapoint", parametersIDSearchdomain);
        Dictionary<int, List<Datapoint>> datapoint_unassigned = [];
        while (datapointReader.Read())
        {
            int id = datapointReader.GetInt32(0);
            int id_entity = datapointReader.GetInt32(1);
            string name = datapointReader.GetString(2);
            string probmethodString = datapointReader.GetString(3);
            string hash = datapointReader.GetString(4);
            ProbMethod probmethod = new(probmethodString, _logger);
            if (embedding_unassigned.TryGetValue(id, out Dictionary<string, float[]>? embeddings) && probmethod is not null)
            {
                embedding_unassigned.Remove(id);
                if (!datapoint_unassigned.ContainsKey(id_entity))
                {
                    datapoint_unassigned[id_entity] = [];
                }
                datapoint_unassigned[id_entity].Add(new Datapoint(name, probmethod, hash, [.. embeddings.Select(kv => (kv.Key, kv.Value))]));
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
                Entity entity = new(attributes, probmethod, datapoints, name)
                {
                    id = id
                };
                entityCache.Add(entity);
            }
        }
        entityReader.Close();
        modelsInUse = GetModels(entityCache);
        embeddingCache = []; // TODO remove this and implement proper remediation to improve performance
    }

    public List<(float, string)> Search(string query, bool sort=true)
    {
        if (!embeddingCache.TryGetValue(query, out Dictionary<string, float[]>? queryEmbeddings))
        {
            queryEmbeddings = Datapoint.GenerateEmbeddings(query, modelsInUse, aIProvider);
            if (embeddingCache.Count < embeddingCacheMaxSize) // TODO add better way of managing cache limit hits
            { // Idea: Add access count to each entry. On limit hit, sort the entries by access count and remove the bottom 10% of entries
                embeddingCache.Add(query, queryEmbeddings);
            }
        } // TODO implement proper cache remediation for embeddingCache here

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
                datapointProbs.Add((datapoint.name, datapoint.probMethod.method(list)));
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
}

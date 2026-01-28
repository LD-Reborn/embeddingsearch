using System.Data.Common;
using System.Text;
using System.Text.Json;
using MySql.Data.MySqlClient;
using Server.Exceptions;
using Server.Models;
using Shared.Models;

namespace Server.Helper;

public class DatabaseHelper(ILogger<DatabaseHelper> logger)
{
    private readonly ILogger<DatabaseHelper> _logger = logger;

    public static SQLHelper GetSQLHelper(EmbeddingSearchOptions embeddingSearchOptions)
    {
        string connectionString = embeddingSearchOptions.ConnectionStrings.SQL;
        MySqlConnection connection = new(connectionString);
        connection.Open();
        return new SQLHelper(connection, connectionString);
    }

    public static void DatabaseInsertEmbeddingBulk(SQLHelper helper, int id_datapoint, List<(string model, byte[] embedding)> data)
    {
        Dictionary<string, object> parameters = [];
        parameters["id_datapoint"] = id_datapoint;
        var query = new StringBuilder("INSERT INTO embedding (id_datapoint, model, embedding) VALUES ");
        foreach (var (model, embedding) in data)
        {
            string modelParam = $"model_{Guid.NewGuid()}".Replace("-", "");
            string embeddingParam = $"embedding_{Guid.NewGuid()}".Replace("-", "");
            parameters[modelParam] = model;
            parameters[embeddingParam] = embedding;

            query.Append($"(@id_datapoint, @{modelParam}, @{embeddingParam}), ");
        }

        query.Length -= 2; // remove trailing comma
        helper.ExecuteSQLNonQuery(query.ToString(), parameters);
    }

    public static int DatabaseInsertEmbeddingBulk(SQLHelper helper, List<(string hash, string model, byte[] embedding)> data)
    {
        return helper.BulkExecuteNonQuery(
            "INSERT INTO embedding (id_datapoint, model, embedding) SELECT d.id, @model, @embedding FROM datapoint d WHERE d.hash = @hash",
            data.Select(element => new object[] {
                new MySqlParameter("@model", element.model),
                new MySqlParameter("@embedding", element.embedding),
                new MySqlParameter("@hash", element.hash)
            })
        );
    }


    public static int DatabaseInsertSearchdomain(SQLHelper helper, string name, SearchdomainSettings settings = new())
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "settings", settings}
        };
        return helper.ExecuteSQLCommandGetInsertedID("INSERT INTO searchdomain (name, settings) VALUES (@name, @settings)", parameters);
    }

    public static int DatabaseInsertEntity(SQLHelper helper, string name, ProbMethodEnum probmethod, int id_searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "probmethod", probmethod.ToString() },
            { "id_searchdomain", id_searchdomain }
        };
        return helper.ExecuteSQLCommandGetInsertedID("INSERT INTO entity (name, probmethod, id_searchdomain) VALUES (@name, @probmethod, @id_searchdomain)", parameters);
    }

    public static int DatabaseInsertAttribute(SQLHelper helper, string attribute, string value, int id_entity)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "attribute", attribute },
            { "value", value },
            { "id_entity", id_entity }
        };
        return helper.ExecuteSQLCommandGetInsertedID("INSERT INTO attribute (attribute, value, id_entity) VALUES (@attribute, @value, @id_entity)", parameters);
    }

    public static int DatabaseInsertAttributes(SQLHelper helper, List<(string attribute, string value, int id_entity)> values) //string[] attribute, string value, int id_entity)
    {
        return helper.BulkExecuteNonQuery(
            "INSERT INTO attribute (attribute, value, id_entity) VALUES (@attribute, @value, @id_entity)",
            values.Select(element => new object[] {
                new MySqlParameter("@attribute", element.attribute),
                new MySqlParameter("@value", element.value),
                new MySqlParameter("@id_entity", element.id_entity)
            })
        );
    }

    public static int DatabaseInsertDatapoints(SQLHelper helper, List<(string name, ProbMethodEnum probmethod_embedding, SimilarityMethodEnum similarityMethod, string hash)> values, int id_entity)
    {
        return helper.BulkExecuteNonQuery(
            "INSERT INTO datapoint (name, probmethod_embedding, similaritymethod, hash, id_entity) VALUES (@name, @probmethod_embedding, @similaritymethod, @hash, @id_entity)",
            values.Select(element => new object[] {
                new MySqlParameter("@name", element.name),
                new MySqlParameter("@probmethod_embedding", element.probmethod_embedding),
                new MySqlParameter("@similaritymethod", element.similarityMethod),
                new MySqlParameter("@hash", element.hash),
                new MySqlParameter("@id_entity", id_entity)
            })
        );
    }

    public static int DatabaseInsertDatapoint(SQLHelper helper, string name, ProbMethodEnum probmethod_embedding, SimilarityMethodEnum similarityMethod, string hash, int id_entity)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "probmethod_embedding", probmethod_embedding.ToString() },
            { "similaritymethod", similarityMethod.ToString() },
            { "hash", hash },
            { "id_entity", id_entity }
        };
        return helper.ExecuteSQLCommandGetInsertedID("INSERT INTO datapoint (name, probmethod_embedding, similaritymethod, hash, id_entity) VALUES (@name, @probmethod_embedding, @similaritymethod, @hash, @id_entity)", parameters);
    }

    public static int DatabaseInsertEmbedding(SQLHelper helper, int id_datapoint, string model, byte[] embedding)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "id_datapoint", id_datapoint },
            { "model", model },
            { "embedding", embedding }
        };
        return helper.ExecuteSQLCommandGetInsertedID("INSERT INTO embedding (id_datapoint, model, embedding) VALUES (@id_datapoint, @model, @embedding)", parameters);
    }

    public int GetSearchdomainID(SQLHelper helper, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "searchdomain", searchdomain}
        };
        lock (helper.connection)
        {
            DbDataReader reader = helper.ExecuteSQLCommand("SELECT id FROM searchdomain WHERE name = @searchdomain", parameters);
            bool success = reader.Read();
            int result = success ? reader.GetInt32(0) : 0;
            reader.Close();
            if (success)
            {
                return result;
            }
            else
            {
                _logger.LogError("Unable to retrieve searchdomain ID for {searchdomain}", [searchdomain]);
                throw new SearchdomainNotFoundException(searchdomain);
            }
        }
    }

    public void RemoveEntity(List<Entity> entityCache, SQLHelper helper, string name, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "searchdomain", GetSearchdomainID(helper, searchdomain)}
        };

        helper.ExecuteSQLNonQuery("DELETE embedding.* FROM embedding JOIN datapoint dp ON id_datapoint = dp.id JOIN entity ON id_entity = entity.id WHERE entity.name = @name AND entity.id_searchdomain = @searchdomain", parameters);
        helper.ExecuteSQLNonQuery("DELETE datapoint.* FROM datapoint JOIN entity ON id_entity = entity.id WHERE entity.name = @name AND entity.id_searchdomain = @searchdomain", parameters);
        helper.ExecuteSQLNonQuery("DELETE attribute.* FROM attribute JOIN entity ON id_entity = entity.id WHERE entity.name = @name AND entity.id_searchdomain = @searchdomain", parameters);
        helper.ExecuteSQLNonQuery("DELETE FROM entity WHERE name = @name AND entity.id_searchdomain = @searchdomain", parameters);
        entityCache.RemoveAll(entity => entity.name == name);
    }

    public int RemoveAllEntities(SQLHelper helper, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "searchdomain", GetSearchdomainID(helper, searchdomain)}
        };

        helper.ExecuteSQLNonQuery("DELETE embedding.* FROM embedding JOIN datapoint dp ON id_datapoint = dp.id JOIN entity ON id_entity = entity.id WHERE entity.id_searchdomain = @searchdomain", parameters);
        helper.ExecuteSQLNonQuery("DELETE datapoint.* FROM datapoint JOIN entity ON id_entity = entity.id WHERE entity.id_searchdomain = @searchdomain", parameters);
        helper.ExecuteSQLNonQuery("DELETE FROM attribute WHERE id_entity IN (SELECT entity.id FROM entity WHERE id_searchdomain = @searchdomain)", parameters);
        return helper.ExecuteSQLNonQuery("DELETE FROM entity WHERE entity.id_searchdomain = @searchdomain", parameters);
    }

    public bool HasEntity(SQLHelper helper, string name, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "searchdomain", GetSearchdomainID(helper, searchdomain)}
        };
        lock (helper.connection)
        {
            DbDataReader reader = helper.ExecuteSQLCommand("SELECT COUNT(*) FROM entity WHERE name = @name AND id_searchdomain = @searchdomain", parameters);
            bool success = reader.Read();
            bool result = success && reader.GetInt32(0) > 0;
            reader.Close();
            if (success)
            {
                return result;
            }
            else
            {
                _logger.LogError("Unable to determine whether an entity named {name} exists for {searchdomain}", [name, searchdomain]);
                throw new Exception($"Unable to determine whether an entity named {name} exists for {searchdomain}");
            }
        }
    }

    public int? GetEntityID(SQLHelper helper, string name, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "searchdomain", GetSearchdomainID(helper, searchdomain)}
        };
        lock (helper.connection)
        {
            DbDataReader reader = helper.ExecuteSQLCommand("SELECT id FROM entity WHERE name = @name AND id_searchdomain = @searchdomain", parameters);
            bool success = reader.Read();
            int? result = success ? reader.GetInt32(0) : 0;
            reader.Close();
            return result;
        }
    }

    public static long GetSearchdomainDatabaseSize(SQLHelper helper, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "searchdomain", searchdomain}
        };
        DbDataReader searchdomainSumReader = helper.ExecuteSQLCommand("SELECT SUM(LENGTH(id) + LENGTH(name) + LENGTH(settings)) AS total_bytes FROM embeddingsearch.searchdomain WHERE name=@searchdomain", parameters);
        bool success = searchdomainSumReader.Read();
        long result = success && !searchdomainSumReader.IsDBNull(0) ? searchdomainSumReader.GetInt64(0) : 0;
        searchdomainSumReader.Close();

        DbDataReader entitySumReader = helper.ExecuteSQLCommand("SELECT SUM(LENGTH(e.id) + LENGTH(e.name) + LENGTH(e.probmethod) + LENGTH(e.id_searchdomain)) AS total_bytes FROM embeddingsearch.entity e JOIN embeddingsearch.searchdomain s ON e.id_searchdomain = s.id WHERE s.name=@searchdomain", parameters);
        success = entitySumReader.Read();
        result += success && !entitySumReader.IsDBNull(0) ? entitySumReader.GetInt64(0) : 0;
        entitySumReader.Close();

        DbDataReader datapointSumReader = helper.ExecuteSQLCommand("SELECT SUM(LENGTH(d.id) + LENGTH(d.name) + LENGTH(d.probmethod_embedding) + LENGTH(d.similaritymethod) + LENGTH(d.id_entity) + LENGTH(d.hash)) AS total_bytes FROM embeddingsearch.datapoint d JOIN embeddingsearch.entity e ON d.id_entity = e.id JOIN embeddingsearch.searchdomain s ON e.id_searchdomain = s.id WHERE s.name=@searchdomain", parameters);
        success = datapointSumReader.Read();
        result += success && !datapointSumReader.IsDBNull(0) ? datapointSumReader.GetInt64(0) : 0;
        datapointSumReader.Close();

        DbDataReader embeddingSumReader = helper.ExecuteSQLCommand("SELECT SUM(LENGTH(em.id) + LENGTH(em.id_datapoint) + LENGTH(em.model) + LENGTH(em.embedding)) AS total_bytes FROM embeddingsearch.embedding em JOIN embeddingsearch.datapoint d ON em.id_datapoint = d.id JOIN embeddingsearch.entity e ON d.id_entity = e.id JOIN embeddingsearch.searchdomain s ON e.id_searchdomain = s.id WHERE s.name=@searchdomain", parameters);
        success = embeddingSumReader.Read();
        result += success && !embeddingSumReader.IsDBNull(0) ? embeddingSumReader.GetInt64(0) : 0;
        embeddingSumReader.Close();

        DbDataReader attributeSumReader = helper.ExecuteSQLCommand("SELECT SUM(LENGTH(a.id) + LENGTH(a.id_entity) + LENGTH(a.attribute) + LENGTH(a.value)) AS total_bytes FROM embeddingsearch.attribute a JOIN embeddingsearch.entity e ON a.id_entity = e.id JOIN embeddingsearch.searchdomain s ON e.id_searchdomain = s.id WHERE s.name=@searchdomain", parameters);
        success = attributeSumReader.Read();
        result += success && !attributeSumReader.IsDBNull(0) ? attributeSumReader.GetInt64(0) : 0;
        attributeSumReader.Close();

        return result;
    }

    public static long GetTotalDatabaseSize(SQLHelper helper)
    {
        Dictionary<string, dynamic> parameters = [];
        DbDataReader searchdomainSumReader = helper.ExecuteSQLCommand("SELECT SUM(Data_length) FROM information_schema.tables", parameters);
        try
        {
            bool success = searchdomainSumReader.Read();
            long result = success && !searchdomainSumReader.IsDBNull(0) ? searchdomainSumReader.GetInt64(0) : 0;
            return result;
        } finally
        {
            searchdomainSumReader.Close();
        }
    }

    public static async Task<long> CountEntities(SQLHelper helper)
    {
        DbDataReader searchdomainSumReader = helper.ExecuteSQLCommand("SELECT COUNT(*) FROM entity;", []);
        bool success = searchdomainSumReader.Read();
        long result = success && !searchdomainSumReader.IsDBNull(0) ? searchdomainSumReader.GetInt64(0) : 0;
        searchdomainSumReader.Close();
        return result;
    }

    public static long CountEntitiesForSearchdomain(SQLHelper helper, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "searchdomain", searchdomain}
        };
        DbDataReader searchdomainSumReader = helper.ExecuteSQLCommand("SELECT COUNT(*) FROM entity e JOIN searchdomain s on e.id_searchdomain = s.id WHERE e.id_searchdomain = s.id AND s.name = @searchdomain;", parameters);
        bool success = searchdomainSumReader.Read();
        long result = success && !searchdomainSumReader.IsDBNull(0) ? searchdomainSumReader.GetInt64(0) : 0;
        searchdomainSumReader.Close();
        return result;
    }

    public static SearchdomainSettings GetSearchdomainSettings(SQLHelper helper, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            ["name"] = searchdomain
        };
        DbDataReader reader = helper.ExecuteSQLCommand("SELECT settings from searchdomain WHERE name = @name", parameters);
        try
        {
            reader.Read();
            string settingsString = reader.GetString(0);
            return JsonSerializer.Deserialize<SearchdomainSettings>(settingsString);
        } finally
        {
            reader.Close();
        }
    }
}
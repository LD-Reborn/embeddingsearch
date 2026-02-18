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

    public static async Task DatabaseInsertEmbeddingBulk(SQLHelper helper, int id_datapoint, List<(string model, byte[] embedding)> data, int id_entity, int id_searchdomain)
    {
        Dictionary<string, object> parameters = [];
        parameters["id_datapoint"] = id_datapoint;
        parameters["id_entity"] = id_entity;
        parameters["id_searchdomain"] = id_searchdomain;
        var query = new StringBuilder("INSERT INTO embedding (id_datapoint, model, embedding, id_embedding, id_searchdomain) VALUES ");
        foreach (var (model, embedding) in data)
        {
            string modelParam = $"model_{Guid.NewGuid()}".Replace("-", "");
            string embeddingParam = $"embedding_{Guid.NewGuid()}".Replace("-", "");
            parameters[modelParam] = model;
            parameters[embeddingParam] = embedding;

            query.Append($"(@id_datapoint, @{modelParam}, @{embeddingParam}, @id_entity), ");
        }

        query.Length -= 2; // remove trailing comma
        await helper.ExecuteSQLNonQuery(query.ToString(), parameters);
    }

    public static async Task<int> DatabaseInsertEmbeddingBulk(SQLHelper helper, List<(int id_datapoint, string model, byte[] embedding)> data, int id_entity, int id_searchdomain)
    {
        return await helper.BulkExecuteNonQuery(
            "INSERT INTO embedding (id_datapoint, model, embedding, id_entity, id_searchdomain) VALUES (@id_datapoint, @model, @embedding, @id_entity, @id_searchdomain);",
            data.Select(element => new object[] {
                new MySqlParameter("@model", element.model),
                new MySqlParameter("@embedding", element.embedding),
                new MySqlParameter("@id_datapoint", element.id_datapoint),
                new MySqlParameter("@id_entity", id_entity),
                new MySqlParameter("@id_searchdomain", id_searchdomain)
            })
        );
    }


    public static async Task<int> DatabaseInsertSearchdomain(SQLHelper helper, string name, SearchdomainSettings settings = new())
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "settings", settings}
        };
        return await helper.ExecuteSQLCommandGetInsertedID("INSERT INTO searchdomain (name, settings) VALUES (@name, @settings)", parameters);
    }

    public static async Task<int> DatabaseInsertEntity(SQLHelper helper, string name, ProbMethodEnum probmethod, int id_searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "probmethod", probmethod.ToString() },
            { "id_searchdomain", id_searchdomain }
        };
        return await helper.ExecuteSQLCommandGetInsertedID("INSERT INTO entity (name, probmethod, id_searchdomain) VALUES (@name, @probmethod, @id_searchdomain);", parameters);
    }

    public static async Task<int> DatabaseInsertAttributes(SQLHelper helper, List<(string attribute, string value, int id_entity)> values) //string[] attribute, string value, int id_entity)
    {
        return await helper.BulkExecuteNonQuery(
            "INSERT INTO attribute (attribute, value, id_entity) VALUES (@attribute, @value, @id_entity);",
            values.Select(element => new object[] {
                new MySqlParameter("@attribute", element.attribute),
                new MySqlParameter("@value", element.value),
                new MySqlParameter("@id_entity", element.id_entity)
            })
        );
    }

    public static async Task<int> DatabaseUpdateAttributes(SQLHelper helper, List<(string attribute, string value, int id_entity)> values)
    {
        return await helper.BulkExecuteNonQuery(
            "UPDATE attribute SET value=@value WHERE id_entity=@id_entity AND attribute=@attribute",
            values.Select(element => new object[] {
                new MySqlParameter("@attribute", element.attribute),
                new MySqlParameter("@value", element.value),
                new MySqlParameter("@id_entity", element.id_entity)
            })
        );
    }

    public static async Task<int> DatabaseDeleteAttributes(SQLHelper helper, List<(string attribute, int id_entity)> values)
    {
        return await helper.BulkExecuteNonQuery(
            "DELETE FROM attribute WHERE id_entity=@id_entity AND attribute=@attribute",
            values.Select(element => new object[] {
                new MySqlParameter("@attribute", element.attribute),
                new MySqlParameter("@id_entity", element.id_entity)
            })
        );
    }

    public static async Task<int> DatabaseInsertDatapoints(SQLHelper helper, List<(string name, ProbMethodEnum probmethod_embedding, SimilarityMethodEnum similarityMethod, string hash)> values, int id_entity)
    {
        return await helper.BulkExecuteNonQuery(
            "INSERT INTO datapoint (name, probmethod_embedding, similaritymethod, hash, id_entity) VALUES (@name, @probmethod_embedding, @similaritymethod, @hash, @id_entity);",
            values.Select(element => new object[] {
                new MySqlParameter("@name", element.name),
                new MySqlParameter("@probmethod_embedding", element.probmethod_embedding),
                new MySqlParameter("@similaritymethod", element.similarityMethod),
                new MySqlParameter("@hash", element.hash),
                new MySqlParameter("@id_entity", id_entity)
            })
        );
    }

    public static async Task<int> DatabaseInsertDatapoint(SQLHelper helper, string name, ProbMethodEnum probmethod_embedding, SimilarityMethodEnum similarityMethod, string hash, int id_entity)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "probmethod_embedding", probmethod_embedding.ToString() },
            { "similaritymethod", similarityMethod.ToString() },
            { "hash", hash },
            { "id_entity", id_entity }
        };
        return await helper.ExecuteSQLCommandGetInsertedID("INSERT INTO datapoint (name, probmethod_embedding, similaritymethod, hash, id_entity) VALUES (@name, @probmethod_embedding, @similaritymethod, @hash, @id_entity)", parameters);
    }

    public static async Task<(int datapoints, int embeddings)> DatabaseDeleteEmbeddingsAndDatapoints(SQLHelper helper, List<string> values, int id_entity)
    {
        int embeddings = await helper.BulkExecuteNonQuery(
            "DELETE e FROM embedding e WHERE id_entity = @entityId",
            values.Select(element => new object[] {
                new MySqlParameter("@datapointName", element),
                new MySqlParameter("@entityId", id_entity)
            })
        );
        int datapoints = await helper.BulkExecuteNonQuery(
            "DELETE FROM datapoint WHERE name=@datapointName AND id_entity=@entityId",
            values.Select(element => new object[] {
                new MySqlParameter("@datapointName", element),
                new MySqlParameter("@entityId", id_entity)
            })
        );
        return (datapoints: datapoints, embeddings: embeddings);
    }

    public static async Task<int> DatabaseUpdateDatapoint(SQLHelper helper, List<(string name, string probmethod_embedding, string similarityMethod)> values, int id_entity)
    {
        return await helper.BulkExecuteNonQuery(
            "UPDATE datapoint SET probmethod_embedding=@probmethod, similaritymethod=@similaritymethod WHERE id_entity=@entityId AND name=@datapointName",
            values.Select(element => new object[] {
                new MySqlParameter("@probmethod", element.probmethod_embedding),
                new MySqlParameter("@similaritymethod", element.similarityMethod),
                new MySqlParameter("@entityId", id_entity),
                new MySqlParameter("@datapointName", element.name)
            })
        );
    }

    public static async Task<int> DatabaseInsertEmbedding(SQLHelper helper, int id_datapoint, string model, byte[] embedding, int id_entity, int id_searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "id_datapoint", id_datapoint },
            { "model", model },
            { "embedding", embedding },
            { "id_entity", id_entity },
            { "id_searchdomain", id_searchdomain }
        };
        return await helper.ExecuteSQLCommandGetInsertedID("INSERT INTO embedding (id_datapoint, model, embedding, id_entity, id_searchdomain) VALUES (@id_datapoint, @model, @embedding, @id_entity, @id_searchdomain)", parameters);
    }

    public async Task<int> GetSearchdomainID(SQLHelper helper, string searchdomain)
    {
        Dictionary<string, object?> parameters = new()
        {
            { "searchdomain", searchdomain}
        };
        return (await helper.ExecuteQueryAsync("SELECT id FROM searchdomain WHERE name = @searchdomain", parameters, x => x.GetInt32(0))).First();
    }

    public async Task RemoveEntity(List<Entity> entityCache, SQLHelper helper, string name, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "searchdomain", await GetSearchdomainID(helper, searchdomain)}
        };

        await helper.ExecuteSQLNonQuery("DELETE embedding.* FROM embedding JOIN entity ON id_entity = entity.id WHERE entity.name = @name AND entity.id_searchdomain = @searchdomain", parameters);
        await helper.ExecuteSQLNonQuery("DELETE datapoint.* FROM datapoint JOIN entity ON id_entity = entity.id WHERE entity.name = @name AND entity.id_searchdomain = @searchdomain", parameters);
        await helper.ExecuteSQLNonQuery("DELETE attribute.* FROM attribute JOIN entity ON id_entity = entity.id WHERE entity.name = @name AND entity.id_searchdomain = @searchdomain", parameters);
        await helper.ExecuteSQLNonQuery("DELETE FROM entity WHERE name = @name AND entity.id_searchdomain = @searchdomain", parameters);
        entityCache.RemoveAll(entity => entity.name == name);
    }

    public async Task<int> RemoveAllEntities(SQLHelper helper, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "searchdomain", await GetSearchdomainID(helper, searchdomain)}
        };
        int count;
        do
        {
            count = await helper.ExecuteSQLNonQuery("DELETE FROM embedding WHERE id_searchdomain = @searchdomain LIMIT 10000", parameters);
        } while (count == 10000);
        do
        {
            count = await helper.ExecuteSQLNonQuery("DELETE FROM datapoint WHERE id_entity IN (SELECT id FROM entity WHERE id_searchdomain = @searchdomain) LIMIT 10000", parameters);
        } while (count == 10000);
        do
        {
            count = await helper.ExecuteSQLNonQuery("DELETE FROM attribute WHERE id_entity IN (SELECT id FROM entity WHERE id_searchdomain = @searchdomain) LIMIT 10000", parameters);
        } while (count == 10000);
        int total = 0;
        do
        {
            count = await helper.ExecuteSQLNonQuery("DELETE FROM entity WHERE id_searchdomain = @searchdomain LIMIT 10000", parameters);
            total += count;
        } while (count == 10000);
        return total;
    }

    public async Task<bool> HasEntity(SQLHelper helper, string name, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "searchdomain", await GetSearchdomainID(helper, searchdomain)}
        };
        lock (helper.connection)
        {
            DbDataReader reader = helper.ExecuteSQLCommand("SELECT COUNT(*) FROM entity WHERE name = @name AND id_searchdomain = @searchdomain", parameters);
            try
            {
                bool success = reader.Read();
                bool result = success && reader.GetInt32(0) > 0;
                if (success)
                {
                    return result;
                }
                else
                {
                    _logger.LogError("Unable to determine whether an entity named {name} exists for {searchdomain}", [name, searchdomain]);
                    throw new Exception($"Unable to determine whether an entity named {name} exists for {searchdomain}");
                }                
            } finally
            {
                reader.Close();
            }
        }
    }

    public async Task<int?> GetEntityID(SQLHelper helper, string name, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "searchdomain", await GetSearchdomainID(helper, searchdomain)}
        };
        lock (helper.connection)
        {
            DbDataReader reader = helper.ExecuteSQLCommand("SELECT id FROM entity WHERE name = @name AND id_searchdomain = @searchdomain", parameters);
            try
            {
                bool success = reader.Read();
                int? result = success ? reader.GetInt32(0) : 0;
                return result;
            } finally
            {
                reader.Close();
            }
        }
    }

    public static long GetSearchdomainDatabaseSize(SQLHelper helper, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "searchdomain", searchdomain}
        };
        DbDataReader searchdomainSumReader = helper.ExecuteSQLCommand("SELECT SUM(LENGTH(id) + LENGTH(name) + LENGTH(settings)) AS total_bytes FROM embeddingsearch.searchdomain WHERE name=@searchdomain", parameters);
        bool success;
        long result;
        try
        {
            success = searchdomainSumReader.Read();
            result = success && !searchdomainSumReader.IsDBNull(0) ? searchdomainSumReader.GetInt64(0) : 0;
        } finally
        {
            searchdomainSumReader.Close();
        }

        DbDataReader entitySumReader = helper.ExecuteSQLCommand("SELECT SUM(LENGTH(e.id) + LENGTH(e.name) + LENGTH(e.probmethod) + LENGTH(e.id_searchdomain)) AS total_bytes FROM embeddingsearch.entity e JOIN embeddingsearch.searchdomain s ON e.id_searchdomain = s.id WHERE s.name=@searchdomain", parameters);
        try
        {
            success = entitySumReader.Read();
            result += success && !entitySumReader.IsDBNull(0) ? entitySumReader.GetInt64(0) : 0;
        } finally
        {
            entitySumReader.Close();
        }

        DbDataReader datapointSumReader = helper.ExecuteSQLCommand("SELECT SUM(LENGTH(d.id) + LENGTH(d.name) + LENGTH(d.probmethod_embedding) + LENGTH(d.similaritymethod) + LENGTH(d.id_entity) + LENGTH(d.hash)) AS total_bytes FROM embeddingsearch.datapoint d JOIN embeddingsearch.entity e ON d.id_entity = e.id JOIN embeddingsearch.searchdomain s ON e.id_searchdomain = s.id WHERE s.name=@searchdomain", parameters);
        try
        {
            success = datapointSumReader.Read();
            result += success && !datapointSumReader.IsDBNull(0) ? datapointSumReader.GetInt64(0) : 0;
        } finally
        {
            datapointSumReader.Close();
        }

        DbDataReader embeddingSumReader = helper.ExecuteSQLCommand("SELECT SUM(LENGTH(em.id) + LENGTH(em.id_datapoint) + LENGTH(em.model) + LENGTH(em.embedding)) AS total_bytes FROM embeddingsearch.embedding em JOIN embeddingsearch.datapoint d ON em.id_datapoint = d.id JOIN embeddingsearch.entity e ON d.id_entity = e.id JOIN embeddingsearch.searchdomain s ON e.id_searchdomain = s.id WHERE s.name=@searchdomain", parameters);
        try
        {
            success = embeddingSumReader.Read();
            result += success && !embeddingSumReader.IsDBNull(0) ? embeddingSumReader.GetInt64(0) : 0;
        } finally
        {
            embeddingSumReader.Close();
        }

        DbDataReader attributeSumReader = helper.ExecuteSQLCommand("SELECT SUM(LENGTH(a.id) + LENGTH(a.id_entity) + LENGTH(a.attribute) + LENGTH(a.value)) AS total_bytes FROM embeddingsearch.attribute a JOIN embeddingsearch.entity e ON a.id_entity = e.id JOIN embeddingsearch.searchdomain s ON e.id_searchdomain = s.id WHERE s.name=@searchdomain", parameters);
        try
        {
            success = attributeSumReader.Read();
            result += success && !attributeSumReader.IsDBNull(0) ? attributeSumReader.GetInt64(0) : 0;
        } finally
        {
            attributeSumReader.Close();
        }

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

    public static long CountEntitiesForSearchdomain(SQLHelper helper, string searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "searchdomain", searchdomain}
        };
        DbDataReader searchdomainSumReader = helper.ExecuteSQLCommand("SELECT COUNT(*) FROM entity e JOIN searchdomain s on e.id_searchdomain = s.id WHERE e.id_searchdomain = s.id AND s.name = @searchdomain;", parameters);
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
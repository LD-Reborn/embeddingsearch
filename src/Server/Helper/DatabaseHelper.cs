using System.Data.Common;
using System.Text;

namespace Server.Helper;

public class DatabaseHelper(ILogger<DatabaseHelper> logger)
{
    private readonly ILogger<DatabaseHelper> _logger = logger;

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

    public static int DatabaseInsertSearchdomain(SQLHelper helper, string name)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "settings", "{}"} // TODO add settings. It's not used yet, but maybe it's needed someday...
        };
        return helper.ExecuteSQLCommandGetInsertedID("INSERT INTO searchdomain (name, settings) VALUES (@name, @settings)", parameters);
    }

    public static int DatabaseInsertEntity(SQLHelper helper, string name, string probmethod, int id_searchdomain)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "probmethod", probmethod },
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

    public static int DatabaseInsertDatapoint(SQLHelper helper, string name, string probmethod_embedding, string similarityMethod, string hash, int id_entity)
    {
        Dictionary<string, dynamic> parameters = new()
        {
            { "name", name },
            { "probmethod_embedding", probmethod_embedding },
            { "similaritymethod", similarityMethod },
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
                throw new Exception($"Unable to retrieve searchdomain ID for {searchdomain}");
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
        helper.ExecuteSQLNonQuery("DELETE attribute.* FROM attribute JOIN entity ON id_entity = entity.id WHERE entity.id_searchdomain = @searchdomain", parameters);
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
                throw new Exception($"Unable to determine whether an entity named {name} exists for {searchdomain}"); // TODO implement logging here; add logger via method injection
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
}
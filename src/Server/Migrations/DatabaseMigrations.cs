using System.Data.Common;
using Server.Exceptions;
using Server.Helper;
using System.Reflection;

namespace Server.Migrations;

public static class DatabaseMigrations
{
    public static void Migrate(SQLHelper helper)
    {
        int initialDatabaseVersion = DatabaseGetVersion(helper);
        int databaseVersion = initialDatabaseVersion;

        if (databaseVersion == 0)
        {
            databaseVersion = Create(helper);
        }

        var updateMethods = typeof(DatabaseMigrations)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("UpdateFrom") && m.ReturnType == typeof(int))
            .OrderBy(m => int.Parse(m.Name["UpdateFrom".Length..]))
            .ToList();

        foreach (var method in updateMethods)
        {
            var version = int.Parse(method.Name["UpdateFrom".Length..]);
            if (version >= databaseVersion)
            {
                databaseVersion = (int)method.Invoke(null, new object[] { helper });
            }
        }

        if (databaseVersion != initialDatabaseVersion)
        {
            var _ = helper.ExecuteSQLNonQuery("UPDATE settings SET value = @databaseVersion", new() { ["databaseVersion"] = databaseVersion.ToString() }).Result;
        }
    }

    public static int DatabaseGetVersion(SQLHelper helper)
    {
        DbDataReader reader = helper.ExecuteSQLCommand("show tables", []);
        try
        {
            bool hasTables = reader.Read();
            if (!hasTables)
            {
                return 0;
            }
        } finally
        {
            reader.Close();
        }

        reader = helper.ExecuteSQLCommand("show tables like '%settings%'", []);
        try
        {
            bool hasSystemTable = reader.Read();
            if (!hasSystemTable)
            {
                return 1;
            }
        } finally
        {
            reader.Close();
        }
        reader = helper.ExecuteSQLCommand("SELECT value FROM settings WHERE name=\"DatabaseVersion\"", []);
        try
        {
            reader.Read();
            string rawVersion = reader.GetString(0);
            bool success = int.TryParse(rawVersion, out int version);
            if (!success)
            {
                throw new DatabaseVersionException();
            }
            return version;
        } finally
        {
            reader.Close();
        }
    }

    public static int Create(SQLHelper helper)
    {
        var _ = helper.ExecuteSQLNonQuery("CREATE TABLE searchdomain (id int PRIMARY KEY auto_increment, name varchar(512), settings JSON);", []).Result;
        _ = helper.ExecuteSQLNonQuery("CREATE TABLE entity (id int PRIMARY KEY auto_increment, name varchar(512), probmethod varchar(128), id_searchdomain int, FOREIGN KEY (id_searchdomain) REFERENCES searchdomain(id));", []).Result;
        _ = helper.ExecuteSQLNonQuery("CREATE TABLE attribute (id int PRIMARY KEY auto_increment, id_entity int, attribute varchar(512), value longtext, FOREIGN KEY (id_entity) REFERENCES entity(id));", []).Result;
        _ = helper.ExecuteSQLNonQuery("CREATE TABLE datapoint (id int PRIMARY KEY auto_increment, name varchar(512), probmethod_embedding varchar(512), id_entity int, FOREIGN KEY (id_entity) REFERENCES entity(id));", []).Result;
        _ = helper.ExecuteSQLNonQuery("CREATE TABLE embedding (id int PRIMARY KEY auto_increment, id_datapoint int, model varchar(512), embedding blob, FOREIGN KEY (id_datapoint) REFERENCES datapoint(id));", []).Result;
        return 1;
    }

    public static int UpdateFrom1(SQLHelper helper)
    {
        var _ = helper.ExecuteSQLNonQuery("CREATE TABLE settings (name varchar(512), value varchar(8192));", []).Result;
        _ = helper.ExecuteSQLNonQuery("INSERT INTO settings (name, value) VALUES (\"DatabaseVersion\", \"2\");", []).Result;
        return 2;
    }

    public static int UpdateFrom2(SQLHelper helper)
    {
        var _ = helper.ExecuteSQLNonQuery("ALTER TABLE datapoint ADD hash VARCHAR(44);", []).Result;
        _ = helper.ExecuteSQLNonQuery("UPDATE datapoint SET hash='';", []).Result;
        return 3;
    }

    public static int UpdateFrom3(SQLHelper helper)
    {
        var _ = helper.ExecuteSQLNonQuery("ALTER TABLE datapoint ADD COLUMN similaritymethod VARCHAR(512) NULL DEFAULT 'Cosine' AFTER probmethod_embedding", []).Result;
        return 4;
    }

    public static int UpdateFrom4(SQLHelper helper)
    {
        var _ = helper.ExecuteSQLNonQuery("UPDATE searchdomain SET settings = JSON_SET(settings, '$.QueryCacheSize', 1000000) WHERE JSON_EXTRACT(settings, '$.QueryCacheSize') is NULL;", []).Result; // Set QueryCacheSize to a default of 1000000
        return 5;
    }

    public static int UpdateFrom5(SQLHelper helper)
    {
        // Add id_entity to embedding
        var _ = helper.ExecuteSQLNonQuery("ALTER TABLE embedding ADD COLUMN id_entity INT NULL", []).Result;
        int count;
        do
        {
            count = helper.ExecuteSQLNonQuery("UPDATE embedding e JOIN datapoint d ON d.id = e.id_datapoint JOIN (SELECT id FROM embedding WHERE id_entity IS NULL LIMIT 10000) x on x.id = e.id SET e.id_entity = d.id_entity;", []).Result;
        } while (count == 10000);
        
        _ = helper.ExecuteSQLNonQuery("ALTER TABLE embedding MODIFY id_entity INT NOT NULL;", []).Result;
        _ = helper.ExecuteSQLNonQuery("CREATE INDEX idx_embedding_entity_model ON embedding (id_entity, model)", []).Result;

        // Add id_searchdomain to embedding
        _ = helper.ExecuteSQLNonQuery("ALTER TABLE embedding ADD COLUMN id_searchdomain INT NULL", []).Result;
        do
        {
            count = helper.ExecuteSQLNonQuery("UPDATE embedding e JOIN entity en ON en.id = e.id_entity JOIN (SELECT id FROM embedding WHERE id_searchdomain IS NULL LIMIT 10000) x on x.id = e.id SET e.id_searchdomain = en.id_searchdomain;", []).Result;
        } while (count == 10000);
        
        _ = helper.ExecuteSQLNonQuery("ALTER TABLE embedding MODIFY id_searchdomain INT NOT NULL;", []).Result;
        _ = helper.ExecuteSQLNonQuery("CREATE INDEX idx_embedding_searchdomain_model ON embedding (id_searchdomain)", []).Result;

        return 6;
    }
}
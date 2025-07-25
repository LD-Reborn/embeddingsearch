using System.Data.Common;
using Server.Exceptions;
using Server.Helper;

namespace Server.Migrations;

public static class DatabaseMigrations
{
    public static void Migrate(SQLHelper helper)
    {
        int databaseVersion = DatabaseGetVersion(helper);
        switch (databaseVersion)
        {
            case 0:
                databaseVersion = Create(helper);
                goto case 1; // Here lies a dead braincell.
            case 1:
                databaseVersion = UpdateFrom1(helper); // TODO: Implement reflection based dynamic invocation.
                goto case 2;
            case 2:
                databaseVersion = UpdateFrom2(helper);
                goto case 3;
            case 3:
            default:
                break;
        }
        helper.ExecuteSQLNonQuery("UPDATE settings SET value = @databaseVersion", new() { ["databaseVersion"] = databaseVersion.ToString() });
    }
    public static int DatabaseGetVersion(SQLHelper helper)
    {
        DbDataReader reader = helper.ExecuteSQLCommand("show tables", []);
        bool hasTables = reader.Read();
        reader.Close();
        if (!hasTables)
        {
            return 0;
        }

        reader = helper.ExecuteSQLCommand("show tables like '%settings%'", []);
        bool hasSystemTable = reader.Read();
        reader.Close();
        if (!hasSystemTable)
        {
            return 1;
        }
        reader = helper.ExecuteSQLCommand("SELECT value FROM settings WHERE name=\"DatabaseVersion\"", []);
        reader.Read();
        string rawVersion = reader.GetString(0);
        reader.Close();
        bool success = int.TryParse(rawVersion, out int version);
        if (!success)
        {
            throw new DatabaseVersionException();
        }
        return version;
    }

    public static int Create(SQLHelper helper)
    {
        helper.ExecuteSQLNonQuery("CREATE TABLE searchdomain (id int PRIMARY KEY auto_increment, name varchar(512), settings JSON);", []);
        helper.ExecuteSQLNonQuery("CREATE TABLE entity (id int PRIMARY KEY auto_increment, name varchar(512), probmethod varchar(128), id_searchdomain int, FOREIGN KEY (id_searchdomain) REFERENCES searchdomain(id));", []);
        helper.ExecuteSQLNonQuery("CREATE TABLE attribute (id int PRIMARY KEY auto_increment, id_entity int, attribute varchar(512), value longtext, FOREIGN KEY (id_entity) REFERENCES entity(id));", []);
        helper.ExecuteSQLNonQuery("CREATE TABLE datapoint (id int PRIMARY KEY auto_increment, name varchar(512), probmethod_embedding varchar(512), id_entity int, FOREIGN KEY (id_entity) REFERENCES entity(id));", []);
        helper.ExecuteSQLNonQuery("CREATE TABLE embedding (id int PRIMARY KEY auto_increment, id_datapoint int, model varchar(512), embedding blob, FOREIGN KEY (id_datapoint) REFERENCES datapoint(id));", []);
        return 1;
    }

    public static int UpdateFrom1(SQLHelper helper)
    {
        helper.ExecuteSQLNonQuery("CREATE TABLE settings (name varchar(512), value varchar(8192));", []);
        helper.ExecuteSQLNonQuery("INSERT INTO settings (name, value) VALUES (\"DatabaseVersion\", \"2\");", []);
        return 2;
    }

    public static int UpdateFrom2(SQLHelper helper)
    {
        helper.ExecuteSQLNonQuery("ALTER TABLE datapoint ADD hash VARCHAR(44);", []);
        helper.ExecuteSQLNonQuery("UPDATE datapoint SET hash='';", []);
        return 3;
    }
}
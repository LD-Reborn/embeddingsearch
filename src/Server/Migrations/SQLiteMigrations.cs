using System.Data.Common;

public static class SQLiteMigrations
{
    public static void Migrate(DbConnection conn)
    {
        EnableWal(conn);

        using var cmd = conn.CreateCommand();

        cmd.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(cmd.ExecuteScalar());

        if (version == 0)
        {
            CreateV1(conn);
            SetVersion(conn, 1);
            version = 1;
        }

        if (version == 1)
        {
            // future migration
            // UpdateFrom1To2(conn);
            // SetVersion(conn, 2);
        }
    }

    private static void EnableWal(DbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL;";
        cmd.ExecuteNonQuery();
    }


    private static void CreateV1(DbConnection conn)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            CREATE TABLE embedding_cache (
                cache_key   TEXT NOT NULL,
                model_key   TEXT NOT NULL,
                embedding   BLOB NOT NULL,
                idx       INTEGER NOT NULL,
                PRIMARY KEY (cache_key, model_key)
            );

            CREATE INDEX idx_index
                ON embedding_cache(idx);
        """;

        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    private static void SetVersion(DbConnection conn, int version)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }
}

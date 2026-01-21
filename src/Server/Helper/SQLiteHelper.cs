using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Server.Models;
using MySql.Data.MySqlClient;
using System.Configuration;

namespace Server.Helper;

public class SQLiteHelper : SqlHelper, IDisposable
{
    public SQLiteHelper(DbConnection connection, string connectionString) : base(connection, connectionString)
    {
        Connection = connection;
        ConnectionString = connectionString;
    }

    public SQLiteHelper(EmbeddingSearchOptions options) : base(new SqliteConnection(options.ConnectionStrings.Cache), options.ConnectionStrings.Cache ?? "")
    {
        if (options.ConnectionStrings.Cache is null)
        {
            throw new ConfigurationErrorsException("Cache options must not be null when instantiating SQLiteHelper");
        }
        ConnectionString = options.ConnectionStrings.Cache;
        Connection = new SqliteConnection(ConnectionString);
    }

    public override SQLiteHelper DuplicateConnection()
    {
        SqliteConnection newConnection = new(ConnectionString);
        return new SQLiteHelper(newConnection, ConnectionString);
    }

    public override int ExecuteSQLCommandGetInsertedID(string query, object[] parameters)
    {
        lock (Connection)
        {
            EnsureConnected();
            EnsureDbReaderIsClosed();
            using DbCommand command = Connection.CreateCommand();

            command.CommandText = query;
            command.Parameters.AddRange(parameters);
            command.ExecuteNonQuery();
            command.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public int BulkExecuteNonQuery(string sql, IEnumerable<object[]> parameterSets)
    {
        lock (Connection)
        {
            EnsureConnected();
            EnsureDbReaderIsClosed();

            using var transaction = Connection.BeginTransaction();
            using var command = Connection.CreateCommand();

            command.CommandText = sql;
            command.Transaction = transaction;

            int affectedRows = 0;

            foreach (var parameters in parameterSets)
            {
                command.Parameters.Clear();
                command.Parameters.AddRange(parameters);
                affectedRows += command.ExecuteNonQuery();
            }

            transaction.Commit();
            return affectedRows;
        }
    }
}
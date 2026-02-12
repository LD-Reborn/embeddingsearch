using System.Data;
using System.Data.Common;
using MySql.Data.MySqlClient;

namespace Server.Helper;

public class SQLHelper:IDisposable
{
    public MySqlConnection connection;
    public DbDataReader? dbDataReader;
    public string connectionString;
    public SQLHelper(MySqlConnection connection, string connectionString)
    {
        this.connection = connection;
        this.connectionString = connectionString;
    }

    public SQLHelper DuplicateConnection()
    {
        MySqlConnection newConnection = new(connectionString);
        return new SQLHelper(newConnection, connectionString);
    }

    public void Dispose()
    {
        connection.Close();
        GC.SuppressFinalize(this);
    }

    public DbDataReader ExecuteSQLCommand(string query, Dictionary<string, dynamic> parameters)
    {
        lock (connection)
        {
            EnsureConnected();
            EnsureDbReaderIsClosed();
            using MySqlCommand command = connection.CreateCommand();
            command.CommandText = query;
            foreach (KeyValuePair<string, dynamic> parameter in parameters)
            {
                command.Parameters.AddWithValue($"@{parameter.Key}", parameter.Value);
            }
            dbDataReader = command.ExecuteReader();
            return dbDataReader;
        }
    }

    public int ExecuteSQLNonQuery(string query, Dictionary<string, dynamic> parameters)
    {
        lock (connection)
        {
            EnsureConnected();
            EnsureDbReaderIsClosed();
            using MySqlCommand command = connection.CreateCommand();

            command.CommandText = query;
            foreach (KeyValuePair<string, dynamic> parameter in parameters)
            {
                command.Parameters.AddWithValue($"@{parameter.Key}", parameter.Value);
            }
            return command.ExecuteNonQuery();
        }
    }

    public int ExecuteSQLCommandGetInsertedID(string query, Dictionary<string, dynamic> parameters)
    {
        lock (connection)
        {
            EnsureConnected();
            EnsureDbReaderIsClosed();
            using MySqlCommand command = connection.CreateCommand();

            command.CommandText = query;
            foreach (KeyValuePair<string, dynamic> parameter in parameters)
            {
                command.Parameters.AddWithValue($"@{parameter.Key}", parameter.Value);
            }
            command.ExecuteNonQuery();
            command.CommandText = "SELECT LAST_INSERT_ID();";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public int BulkExecuteNonQuery(string sql, IEnumerable<object[]> parameterSets)
    {
        lock (connection)
        {
            EnsureConnected();
            EnsureDbReaderIsClosed();

            int affectedRows = 0;
            int retries = 0;
            
            while (retries <= 3)
            {
                try
                {
                    using var transaction = connection.BeginTransaction();
                    using var command = connection.CreateCommand();

                    command.CommandText = sql;
                    command.Transaction = transaction;

                    foreach (var parameters in parameterSets)
                    {
                        command.Parameters.Clear();
                        command.Parameters.AddRange(parameters);
                        affectedRows += command.ExecuteNonQuery();
                    }
                    
                    transaction.Commit();
                    break;
                }
                catch (Exception)
                {
                    retries++;
                    if (retries > 3)
                        throw;
                    Thread.Sleep(10);
                }
            }
            
            return affectedRows;
        }
    }

    public bool EnsureConnected()
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            try
            {
                connection.Close();
                connection.Open();
            }
            catch (Exception ex)
            {
                ElmahCore.ElmahExtensions.RaiseError(ex);
                throw;
            }
        }
        return true;
    }

    public void EnsureDbReaderIsClosed()
    {
        int counter = 0;
        int sleepTime = 10;
        int timeout = 5000;
        while (!(dbDataReader?.IsClosed ?? true))
        {
            if (counter > timeout / sleepTime)
            {
                TimeoutException ex = new("Unable to ensure dbDataReader is closed");
                ElmahCore.ElmahExtensions.RaiseError(ex);
                throw ex;
            }
            Thread.Sleep(sleepTime);
        }
    }
}
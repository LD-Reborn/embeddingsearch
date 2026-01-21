namespace Server.Models;
using System.Data.Common;

public abstract partial class SqlHelper : IDisposable
{
    public DbConnection Connection { get; set; }
    public DbDataReader? DbDataReader { get; set; }
    public string ConnectionString { get; set; }
    public SqlHelper(DbConnection connection, string connectionString)
    {
        Connection = connection;
        ConnectionString = connectionString;
    }

    public abstract SqlHelper DuplicateConnection();

    public void Dispose()
    {
        Connection.Close();
        GC.SuppressFinalize(this);
    }

    public DbDataReader ExecuteSQLCommand(string query, object[] parameters)
    {
        lock (Connection)
        {
            EnsureConnected();
            EnsureDbReaderIsClosed();
            using DbCommand command = Connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddRange(parameters);
            DbDataReader = command.ExecuteReader();
            return DbDataReader;
        }
    }

    public void ExecuteQuery<T>(string query, object[] parameters, Func<DbDataReader, T> map)
    {
        lock (Connection)
        {
            EnsureConnected();
            EnsureDbReaderIsClosed();

            using var command = Connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddRange(parameters);

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                map(reader);
            }

            return;
        }
    }

    public int ExecuteSQLNonQuery(string query, object[] parameters)
    {
        lock (Connection)
        {
            EnsureConnected();
            EnsureDbReaderIsClosed();
            using DbCommand command = Connection.CreateCommand();

            command.CommandText = query;
            command.Parameters.AddRange(parameters);
            return command.ExecuteNonQuery();
        }
    }

    public abstract int ExecuteSQLCommandGetInsertedID(string query, object[] parameters);

    public bool EnsureConnected()
    {
        if (Connection.State != System.Data.ConnectionState.Open)
        {
            try
            {
                Connection.Close();
                Connection.Open();
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
        while (!(DbDataReader?.IsClosed ?? true))
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
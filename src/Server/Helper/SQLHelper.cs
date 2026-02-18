using System.Data;
using System.Data.Common;
using MySql.Data.MySqlClient;

namespace Server.Helper;

public class SQLHelper:IDisposable
{
    public MySqlConnection connection;
    public DbDataReader? dbDataReader;
    public MySqlConnectionPoolElement[] connectionPool;
    public string connectionString;
    public SQLHelper(MySqlConnection connection, string connectionString)
    {
        this.connection = connection;
        this.connectionString = connectionString;
        connectionPool = new MySqlConnectionPoolElement[50];
        for (int i = 0; i < connectionPool.Length; i++)
        {
            connectionPool[i] = new MySqlConnectionPoolElement(new MySqlConnection(connectionString), new(1, 1));
        }
    }

    public SQLHelper DuplicateConnection() // TODO remove this
    {
        return this;
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

    public async Task<List<T>> ExecuteQueryAsync<T>(
        string sql,
        Dictionary<string, object?> parameters,
        Func<DbDataReader, T> map)
    {
        var poolElement = await GetMySqlConnectionPoolElement();
        var connection = poolElement.connection;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            foreach (var p in parameters)
                command.Parameters.AddWithValue($"@{p.Key}", p.Value);

            await using var reader = await command.ExecuteReaderAsync();

            var result = new List<T>();
            while (await reader.ReadAsync())
            {
                result.Add(map(reader));
            }

            return result;
        } finally
        {

            poolElement.Semaphore.Release();
        }
    }

    public async Task<int> ExecuteSQLNonQuery(string query, Dictionary<string, dynamic> parameters)
    {
        var poolElement = await GetMySqlConnectionPoolElement();
        var connection = poolElement.connection;
        try
        {
            using MySqlCommand command = connection.CreateCommand();

            command.CommandText = query;
            foreach (KeyValuePair<string, dynamic> parameter in parameters)
            {
                command.Parameters.AddWithValue($"@{parameter.Key}", parameter.Value);
            }
            return command.ExecuteNonQuery();
        } finally
        {
            poolElement.Semaphore.Release();
        }
    }

    public async Task<int> ExecuteSQLCommandGetInsertedID(string query, Dictionary<string, dynamic> parameters)
    {
        var poolElement = await GetMySqlConnectionPoolElement();
        var connection = poolElement.connection;
        try
        {
            using MySqlCommand command = connection.CreateCommand();

            command.CommandText = query;
            foreach (KeyValuePair<string, dynamic> parameter in parameters)
            {
                command.Parameters.AddWithValue($"@{parameter.Key}", parameter.Value);
            }
            command.ExecuteNonQuery();
            command.CommandText = "SELECT LAST_INSERT_ID();";
            return Convert.ToInt32(command.ExecuteScalar());
        } finally
        {
            poolElement.Semaphore.Release();
        }
    }

    public async Task<int> BulkExecuteNonQuery(string sql, IEnumerable<object[]> parameterSets)
    {
        var poolElement = await GetMySqlConnectionPoolElement();
        var connection = poolElement.connection;
        try
        {
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
        } finally
        {
            poolElement.Semaphore.Release();
        }
    }

    public async Task<MySqlConnectionPoolElement> GetMySqlConnectionPoolElement()
    {
        int counter = 0;
        int sleepTime = 10;
        do
        {
            foreach (var element in connectionPool)
            {
                if (element.Semaphore.Wait(0))
                {
                    if (element.connection.State == ConnectionState.Closed)
                    {
                        await element.connection.CloseAsync();
                        await element.connection.OpenAsync();
                    }
                    return element;
                }
            }
            Thread.Sleep(sleepTime);
        } while (++counter <= 50);
        TimeoutException ex = new("Unable to get MySqlConnection");
        ElmahCore.ElmahExtensions.RaiseError(ex);
        throw ex;
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

public struct MySqlConnectionPoolElement
{
    public MySqlConnection connection;
    public SemaphoreSlim Semaphore;

    public MySqlConnectionPoolElement(MySqlConnection connection, SemaphoreSlim semaphore)
    {
        this.connection = connection;
        this.Semaphore = semaphore;
    }
}
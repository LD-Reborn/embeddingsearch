using System.Data.Common;
using MySql.Data.MySqlClient;

namespace Server;

public class SQLHelper
{
    public MySqlConnection connection;
    public SQLHelper(MySqlConnection connection)
    {
        this.connection = connection;
    }
    public DbDataReader ExecuteSQLCommand(string query, Dictionary<string, dynamic> parameters)
    {
        lock (connection)
        {
            EnsureConnected();
            using MySqlCommand command = connection.CreateCommand();
            command.CommandText = query;
            foreach (KeyValuePair<string, dynamic> parameter in parameters)
            {
                command.Parameters.AddWithValue($"@{parameter.Key}", parameter.Value);
            }
            return command.ExecuteReader();
        }
    }

    public void ExecuteSQLNonQuery(string query, Dictionary<string, dynamic> parameters)
    {
        lock (connection)
        {
            EnsureConnected();
            using MySqlCommand command = connection.CreateCommand();

            command.CommandText = query;
            foreach (KeyValuePair<string, dynamic> parameter in parameters)
            {
                command.Parameters.AddWithValue($"@{parameter.Key}", parameter.Value);
            }
            command.ExecuteNonQuery();
        }
    }

    public int ExecuteSQLCommandGetInsertedID(string query, Dictionary<string, dynamic> parameters)
    {
        lock (connection)
        {
            EnsureConnected();
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

    public bool EnsureConnected()
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            try
            {
                connection.Close();
                connection.Open();
            }
            catch (Exception)
            {
                throw; // TODO add logging here
            }
        }
        return true;
    }
}
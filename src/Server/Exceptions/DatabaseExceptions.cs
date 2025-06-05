namespace Server.Exceptions;

public class DatabaseVersionException : Exception
{
    public DatabaseVersionException()
        : base("DatabaseVersion could not be parsed as integer. Please ensure the DatabaseVersion can be parsed as an integer.")
    {
    }

    public DatabaseVersionException(string message)
        : base(message)
    {
    }
} 
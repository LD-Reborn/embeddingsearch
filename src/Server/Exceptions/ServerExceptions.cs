namespace server.Exceptions;

public class ServerConfigurationException : Exception
{
    public ServerConfigurationException()
        : base("Configuration is incomplete or was set up incorrectly")
    {
    }

    public ServerConfigurationException(string message)
        : base(message)
    {
    }
}
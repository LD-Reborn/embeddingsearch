namespace Indexer.Exceptions;

public class IndexerConfigurationException : Exception
{
    public IndexerConfigurationException()
        : base("Configuration is incomplete or was set up incorrectly")
    {
    }

    public IndexerConfigurationException(string message)
        : base(message)
    {
    }
}
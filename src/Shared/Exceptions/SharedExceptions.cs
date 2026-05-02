using Server;

namespace Shared.Exceptions;

public class ConfigurationException : Exception
{
    public ConfigurationException()
        : base("Configuration is incomplete or was set up incorrectly")
    {
    }

    public ConfigurationException(string message)
        : base(message)
    {
    }
}

public class JSONPathSelectionException(string path, string testedContent) : Exception($"Unable to select tokens using JSONPath {path} for string: {testedContent}.") { }

public class UnsupportedConfigurationException(AiProviderRequestType aiProviderRequestType, string handler) : Exception($"Unable to select to perform action {aiProviderRequestType} for handler: {handler}. This action might not be supported by the handler.") { }

public class UnknownProviderException(string provider, string model) : Exception($"Unable to find provider {provider} for the model description {model}.") { }

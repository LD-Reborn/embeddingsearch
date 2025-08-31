using System.Timers;

namespace Indexer.Models;

public interface IScript
{
    int Init(ScriptToolSet toolSet);
    int Update(ICallbackInfos callbackInfos);
    int Stop();
}

public class ScriptToolSet
{
    public string FilePath;
    public Client.Client Client;
    public LoggerWrapper Logger;
    public ICallbackInfos? CallbackInfos;
    public IConfiguration Configuration;
    public CancellationToken CancellationToken;
    public string Name;

    public ScriptToolSet(string filePath, Client.Client client, ILogger<WorkerManager> logger, IConfiguration configuration, CancellationToken cancellationToken, string name)
    {
        Configuration = configuration;
        Name = name;
        FilePath = filePath;
        Client = client;
        Logger = new LoggerWrapper(logger);
        CancellationToken = cancellationToken;
    }
}

public class LoggerWrapper
{
    private readonly ILogger _logger;
    public LoggerWrapper(ILogger logger) => _logger = logger;

    public void LogTrace(string message, params object[]? args) => _logger.LogTrace(message, args);
    public void LogDebug(string message, params object[]? args) => _logger.LogDebug(message, args);
    public void LogInformation(string message, params object[]? args) => _logger.LogInformation(message, args);
    public void LogWarning(string message, params object[]? args) => _logger.LogWarning(message, args);
    public void LogError(string message, params object[]? args) => _logger.LogError(message, args);
    public void LogCritical(string message, params object[]? args) => _logger.LogCritical(message, args);
}

public interface ICallbackInfos { }

public class RunOnceCallbackInfos : ICallbackInfos {}

public class IntervalCallbackInfos : ICallbackInfos
{
    public object? sender;
    public required ElapsedEventArgs e;

}

public class ScheduleCallbackInfos : ICallbackInfos {}

public class FileUpdateCallbackInfos : ICallbackInfos
{
    public required object? sender;
    public required FileSystemEventArgs? e;
}

public class ManualTriggerCallbackInfos : ICallbackInfos { }

public struct ScriptUpdateInfo
{
    public DateTime DateTime { get; set; }
    public bool Successful { get; set; }
    public Exception? Exception { get; set; }
}
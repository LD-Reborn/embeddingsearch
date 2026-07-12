using System.Collections.Concurrent;
using System.Timers;
using Indexer.Services;

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
    public IndexerOptions Configuration;
    public CancellationToken CancellationToken;
    public string Name;
    public DocumentProcessor DocumentProcessor;

    public ConcurrentQueue<LogEntry> Logs { get; private set; } = new();
    public event Action<LogEntry>? OnLog;

    public ScriptToolSet(string filePath, Client.Client client, ILogger<WorkerManager> logger, IndexerOptions configuration, CancellationToken cancellationToken, string name, DocumentProcessor documentProcessor)
    {
        Configuration = configuration;
        Name = name;
        FilePath = filePath;
        Client = client;
        Logger = new LoggerWrapper(logger, name, Logs, log => OnLog?.Invoke(log));
        CancellationToken = cancellationToken;
        DocumentProcessor = documentProcessor;
    }

}


public class LoggerWrapper
{
    private readonly ILogger _logger;
    private readonly string _workerName;
    private readonly ConcurrentQueue<LogEntry> _logQueue;
    private readonly Action<LogEntry>? _onLog;

    public LoggerWrapper(ILogger logger, string workerName, ConcurrentQueue<LogEntry> logQueue, Action<LogEntry>? onLog)
    {
        _logger = logger;
        _workerName = workerName;
        _logQueue = logQueue;
        _onLog = onLog;
    }

    public void LogTrace(string message, params object[]? args)
    {
        HandleLogQueue(message, args, null, LogLevel.Trace);
        _logger.LogTrace(message, args);
    }
    public void LogDebug(string message, params object[]? args)
    {
        HandleLogQueue(message, args, null, LogLevel.Debug);
        _logger.LogDebug(message, args);
    }
    public void LogInformation(string message, params object[]? args)
    {
        HandleLogQueue(message, args, null, LogLevel.Information);
        _logger.LogInformation(message, args);
    }
    public void LogWarning(string message, params object[]? args)
    {
        HandleLogQueue(message, args, null, LogLevel.Warning);
        _logger.LogWarning(message, args);
    }
    public void LogError(string message, params object[]? args)
    {
        HandleLogQueue(message, args, null, LogLevel.Error);
        _logger.LogError(message, args);
    }
    public void LogCritical(string message, params object[]? args)
    {
        HandleLogQueue(message, args, null, LogLevel.Critical);
        _logger.LogCritical(message, args);
    }

    private void HandleLogQueue(string message, object[]? args, string? workerCallId, LogLevel logLevel)
    {
        LogEntry logEntry = new() { Message = message, Args = args, Timestamp = DateTime.UtcNow, WorkerCallId = workerCallId, LogLevel = logLevel };
        _logQueue.Enqueue(logEntry);
        _onLog?.Invoke(logEntry);
    }
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
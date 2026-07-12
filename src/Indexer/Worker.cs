using Microsoft.Extensions.Diagnostics.HealthChecks;
using Indexer.Models;

public class Worker
{
    public string Name { get; set; }
    public WorkerConfig Config { get; set; }
    public IScriptContainer ScriptContainer { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; set; }
    public List<ICall> Calls { get; set; }
    public bool IsExecuting { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }

    private readonly List<LogEntry> _recentLogs = [];
    private readonly Lock _logsLock = new();
    private const int MaxRecentLogs = 1000;
    private int _logSequenceCounter = 0;

    public Worker(string name, WorkerConfig workerConfig, IScriptContainer scriptable, CancellationTokenSource cancellationTokenSource)
    {
        Name = name;
        Config = workerConfig;
        ScriptContainer = scriptable;
        CancellationTokenSource = cancellationTokenSource;
        IsExecuting = false;
        Calls = [];
        ScriptContainer.ToolSet.OnLog += OnLogReceived;
    }
    
    private void OnLogReceived(LogEntry logEntry)
    {
        lock (_logsLock)
        {
            logEntry.SequenceId = ++_logSequenceCounter;
            _recentLogs.Add(logEntry);
            
            if (_recentLogs.Count > MaxRecentLogs)
            {
                _recentLogs.RemoveRange(0, _recentLogs.Count - MaxRecentLogs);
            }
        }
    }
    
    public List<LogEntry> GetRecentLogs(int count = 100)
    {
        lock (_logsLock)
        {
            return [.. _recentLogs.Take(count)];
        }
    }
    
    public List<LogEntry> GetLogsSinceSequence(int sequenceId)
    {
        lock (_logsLock)
        {
            return [.. _recentLogs.Where(l => l.SequenceId > sequenceId)];
        }
    }
    
    public Dictionary<string, int> GetLogCounts()
    {
        lock (_logsLock)
        {
            Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
            foreach (LogEntry log in _recentLogs)
            {
                string level = log.LogLevel.ToString();
                counts[level] = counts.GetValueOrDefault(level, 0) + 1;
            }
            return counts;
        }
    }

    public void ClearLogs()
    {
        lock (_logsLock)
        {
            _recentLogs.Clear();
            _logSequenceCounter = 0;
        }
    }

    public HealthCheckResult HealthCheck()
    {
        bool hasDegraded = false;
        bool hasUnhealthy = false;
        foreach (ICall call in Calls)
        {
            HealthCheckResult callHealth = call.HealthCheck();
            if (callHealth.Status != HealthStatus.Healthy)
            {
                hasDegraded |= callHealth.Status == HealthStatus.Degraded;
                hasUnhealthy |= callHealth.Status == HealthStatus.Unhealthy;
            }
        }
        if (hasUnhealthy)
        {
            return HealthCheckResult.Unhealthy(); // TODO: Retrieve and forward the error message for each call
        }
        else if (hasDegraded)
        {
            return HealthCheckResult.Degraded();
        }
        return HealthCheckResult.Healthy();
    }
}
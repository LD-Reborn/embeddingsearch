using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace Indexer.Models;

public class WorkerCollection
{
    public List<Worker> Workers;
    public List<Type> types;
    public WorkerCollection()
    {
        Workers = [];
        types = [typeof(PythonScriptable)];
    }
}

public class Worker
{
    public string Name { get; set; }
    public WorkerConfig Config { get; set; }
    public IScriptable Scriptable { get; set; }
    public List<ICall> Calls { get; set; }

    public Worker(string Name, WorkerConfig workerConfig, IScriptable scriptable)
    {
        this.Name = Name;
        this.Config = workerConfig;
        this.Scriptable = scriptable;
        Calls = [];
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

public class WorkerCollectionConfig
{
    public required List<WorkerConfig> Worker { get; set; }
}

public class WorkerConfig
{
    public required string Name { get; set; }
    public required List<string> Searchdomains { get; set; }
    public required string Script { get; set; }
    public required List<CallConfig> Calls { get; set; }
}

public class CallConfig
{
    public required string Type { get; set; }
    public long? Interval { get; set; } // For Type: Interval
    public string? Path { get; set; } // For Type: FileSystemWatcher
}

public interface ICall
{
    public HealthCheckResult HealthCheck();
}

public class IntervalCall : ICall
{
    public System.Timers.Timer Timer;
    public IScriptable Scriptable;

    public IntervalCall(System.Timers.Timer timer, IScriptable scriptable)
    {
        Timer = timer;
        Scriptable = scriptable;
    }

    public HealthCheckResult HealthCheck()
    {
        if (!Scriptable.UpdateInfo.Successful)
        {
            return HealthCheckResult.Unhealthy();
        }
        double timerInterval = Timer.Interval; // In ms
        DateTime lastRunDateTime = Scriptable.UpdateInfo.DateTime;
        DateTime now = DateTime.Now;
        double millisecondsSinceLastExecution = now.Subtract(lastRunDateTime).TotalMilliseconds;
        if (millisecondsSinceLastExecution >= 2 * timerInterval)
        {
            return HealthCheckResult.Unhealthy();
        }
        return HealthCheckResult.Healthy();
    }

}

public class ScheduleCall : ICall
{
    public HealthCheckResult HealthCheck()
    {
        return HealthCheckResult.Unhealthy(); // Not implemented yet
    }
}

public class FileUpdateCall : ICall
{
    public HealthCheckResult HealthCheck()
    {
        return HealthCheckResult.Unhealthy(); // Not implemented yet
    }
}
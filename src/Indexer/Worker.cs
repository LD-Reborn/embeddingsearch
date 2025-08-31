using Microsoft.Extensions.Diagnostics.HealthChecks;
using Indexer.Models;

public class Worker
{
    public string Name { get; set; }
    public WorkerConfig Config { get; set; }
    public IScriptContainer Scriptable { get; set; }
    public List<ICall> Calls { get; set; }
    public bool IsExecuting { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }

    public Worker(string name, WorkerConfig workerConfig, IScriptContainer scriptable)
    {
        Name = name;
        Config = workerConfig;
        Scriptable = scriptable;
        IsExecuting = false;
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
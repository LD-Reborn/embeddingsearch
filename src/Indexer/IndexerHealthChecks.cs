using System.Collections.ObjectModel;
using Indexer.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Indexer;

public class WorkerHealthCheck : IHealthCheck
{
    private readonly WorkerManager _workerCollection;
    public WorkerHealthCheck(WorkerManager workerCollection)
    {
        _workerCollection = workerCollection;
    }
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        bool hasDegraded = false;
        bool hasUnhealthy = false;
        Dictionary<string, HealthStatus> degradedWorkerList = [];
        foreach (KeyValuePair<string, Worker> workerKVPair in _workerCollection.Workers)
        {
            Worker worker = workerKVPair.Value;
            HealthCheckResult workerHealth = worker.HealthCheck();
            hasDegraded |= workerHealth.Status == HealthStatus.Degraded;
            hasUnhealthy |= workerHealth.Status == HealthStatus.Unhealthy;
            if (workerHealth.Status != HealthStatus.Healthy)
            {
                degradedWorkerList[worker.Name] = workerHealth.Status;
            }
        }
        string degradedWorkerListString = "{" + string.Join(",", [.. degradedWorkerList.Select(kv => '"' + kv.Key + "\": " + kv.Value)]) + "}";
        if (hasUnhealthy)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy(degradedWorkerListString));
        }
        else if (hasDegraded)
        {
            return Task.FromResult(
                HealthCheckResult.Degraded(degradedWorkerListString));
        }
        return Task.FromResult(
                HealthCheckResult.Healthy());
    }
}

using Indexer.Exceptions;
using Indexer.Models;
using ElmahCore;

namespace Indexer.Services;

public class IndexerService : IHostedService
{
    public WorkerCollection workerCollection;
    public ILogger<IndexerService> _logger;

    public IndexerService(WorkerCollection workerCollection, Client.Client client, ILogger<IndexerService> logger)
    {
        this.workerCollection = workerCollection;
        _logger = logger;
        _logger.LogInformation("Initializing IndexerService");
        workerCollection.InitializeWorkers();
        _logger.LogInformation("Initialized IndexerService");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        /*foreach (Worker worker in workerCollection.Workers)
        {
            worker.Scriptable.Init();
        }*/
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
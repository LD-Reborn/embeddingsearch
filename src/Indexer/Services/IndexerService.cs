using Indexer.Models;

namespace Indexer.Services;

public class IndexerService : IHostedService
{
    public WorkerManager workerCollection;
    public ILogger<IndexerService> _logger;

    public IndexerService(WorkerManager workerCollection, Client.Client client, ILogger<IndexerService> logger)
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
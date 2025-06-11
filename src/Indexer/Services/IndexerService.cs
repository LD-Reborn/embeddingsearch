using Indexer.Exceptions;
using Indexer.Models;
using ElmahCore;

namespace Indexer.Services;

public class IndexerService : IHostedService
{
    private readonly WorkerCollection workerCollection;
    private readonly IConfiguration _config;
    private readonly Client.Client client;
    public ILogger<IndexerService> _logger;

    public IndexerService(WorkerCollection workerCollection, IConfiguration configuration, Client.Client client, ILogger<IndexerService> logger, IHttpContextAccessor httpContextAccessor)
    {
        this._config = configuration;
        this.client = client;
        this.workerCollection = workerCollection;
        _logger = logger;
        _logger.LogInformation("Initializing IndexerService");
        // Load and configure all workers
        var sectionMain = _config.GetSection("EmbeddingsearchIndexer");
        if (!sectionMain.Exists())
        {
            _logger.LogCritical("Unable to load section \"EmbeddingsearchIndexer\"");
            throw new IndexerConfigurationException("Unable to load section \"EmbeddingsearchIndexer\"");
        }

        WorkerCollectionConfig? sectionWorker = (WorkerCollectionConfig?)sectionMain.Get(typeof(WorkerCollectionConfig)); //GetValue<WorkerCollectionConfig>("Worker");
        if (sectionWorker is not null)
        {
            foreach (WorkerConfig workerConfig in sectionWorker.Worker)
            {
                _logger.LogInformation("Initializing worker: {Name}", workerConfig.Name);
                if (client.searchdomain == "" && workerConfig.Searchdomains.Count >= 1)
                {
                    client.searchdomain = workerConfig.Searchdomains.First();
                }
                ScriptToolSet toolSet = new(workerConfig.Script, client);
                Worker worker = new(workerConfig.Name, workerConfig, GetScriptable(toolSet));
                workerCollection.Workers.Add(worker);
                foreach (CallConfig callConfig in workerConfig.Calls)
                {
                    _logger.LogInformation("Initializing call of type: {Type}", callConfig.Type);
                    
                    switch (callConfig.Type)
                    {
                        case "interval":
                            if (callConfig.Interval is null)
                            {
                                _logger.LogError("Interval not set for a Call in Worker \"{Name}\"", workerConfig.Name);
                                throw new IndexerConfigurationException($"Interval not set for a Call in Worker \"{workerConfig.Name}\"");
                            }
                            var timer = new System.Timers.Timer((double)callConfig.Interval);
                            timer.Elapsed += (sender, e) =>
                            {
                                try
                                {
                                    worker.Scriptable.Update(new IntervalCallbackInfos() { sender = sender, e = e });
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError("Exception occurred in a Call of Worker \"{name}\": \"{ex}\"", worker.Name, ex.Message);
                                    httpContextAccessor.HttpContext.RaiseError(ex);
                                }
                            };
                            timer.AutoReset = true;
                            timer.Enabled = true;
                            IntervalCall call = new(timer, worker.Scriptable, _logger);
                            worker.Calls.Add(call);
                            break;
                        case "schedule": // TODO implement scheduled tasks using Quartz
                            throw new NotImplementedException("schedule not implemented yet");
                        case "fileupdate":
                            if (callConfig.Path is null)
                            {
                                _logger.LogError("Path not set for a Call in Worker \"{Name}\"", workerConfig.Name);
                                throw new IndexerConfigurationException($"Path not set for a Call in Worker \"{workerConfig.Name}\"");
                            }
                            throw new NotImplementedException("fileupdate not implemented yet");
                        //break;
                        default:
                            throw new IndexerConfigurationException($"Unknown Type specified for a Call in Worker \"{workerConfig.Name}\"");
                    }
                }
            }
        }
        else
        {
            _logger.LogCritical("Unable to load section \"Worker\"");
            throw new IndexerConfigurationException("Unable to load section \"Worker\"");
        }
    }

    public IScriptable GetScriptable(ScriptToolSet toolSet)
    {
        string fileName = toolSet.filePath;
        foreach (Type type in workerCollection.types)
        {
            IScriptable? instance = (IScriptable?)Activator.CreateInstance(type, [toolSet, _logger]);
            if (instance is not null && instance.IsScript(fileName))
            {
                return instance;
            }
        }
        _logger.LogError("Unable to determine the script's language: \"{fileName}\"", fileName);

        throw new UnknownScriptLanguageException(fileName);
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
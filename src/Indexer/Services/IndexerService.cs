using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Indexer.Exceptions;
using Indexer.Models;
using System.Timers;
using Microsoft.AspNetCore.Http.HttpResults;
using embeddingsearch;
using Python.Runtime;

namespace Indexer.Services;

public class IndexerService : IHostedService
{
    private readonly WorkerCollection workerCollection;
    private readonly IConfiguration _config;
    private readonly Client.Client client;

    public IndexerService(WorkerCollection workerCollection, IConfiguration configuration, Client.Client client)
    {
        this._config = configuration;
        this.client = client;
        this.workerCollection = workerCollection;
        // Load and configure all workers
        var sectionMain = _config.GetSection("EmbeddingsearchIndexer");

        WorkerCollectionConfig? sectionWorker = (WorkerCollectionConfig?) sectionMain.Get(typeof(WorkerCollectionConfig)); //GetValue<WorkerCollectionConfig>("Worker");
        if (sectionWorker is not null)
        {
            foreach (WorkerConfig workerConfig in sectionWorker.Worker)
            {
                if (client.searchdomain == "" && workerConfig.Searchdomains.Count >= 1)
                {
                    client.searchdomain = workerConfig.Searchdomains.First();
                }
                ScriptToolSet toolSet = new(workerConfig.Script, client);
                Worker worker = new(workerConfig, GetScriptable(toolSet));
                workerCollection.Workers.Add(worker);
                foreach (Call call in workerConfig.Calls)
                {
                    switch (call.Type)
                    {
                        case "interval":
                            if (call.Interval is null)
                            {
                                throw new IndexerConfigurationException($"Interval not set for a Call in Worker \"{workerConfig.Name}\"");
                            }
                            var timer = new System.Timers.Timer((double)call.Interval);
                            timer.Elapsed += (sender, e) => worker.Scriptable.Update(new IntervalCallbackInfos() { sender = sender, e = e });
                            timer.AutoReset = true;
                            timer.Enabled = true;
                            break;
                        case "schedule": // TODO implement scheduled tasks using Quartz
                            throw new NotImplementedException("schedule not implemented yet");
                        case "fileupdate":
                            if (call.Path is null)
                            {
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
            throw new IndexerConfigurationException("Unable to find section \"Worker\"");
        }
    }

    public IScriptable GetScriptable(ScriptToolSet toolSet)
    {
        string fileName = toolSet.filePath;
        foreach (Type type in workerCollection.types)
        {
            IScriptable? instance = (IScriptable?)Activator.CreateInstance(type, toolSet);
            if (instance is not null && instance.IsScript(fileName))
            {
                return instance;
            }
        }

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
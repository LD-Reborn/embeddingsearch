using ElmahCore;
using Microsoft.AspNetCore.Mvc;
using Indexer.Models;

namespace Indexer.Controllers;

[ApiController]
[Route("[controller]")]
public class WorkerController : ControllerBase
{
    private readonly ILogger<WorkerController> _logger;
    private readonly IConfiguration _config;
    private readonly IConfigurationRoot _configurationRoot;
    private readonly WorkerManager _workerCollection;

    public WorkerController(ILogger<WorkerController> logger, IConfiguration config, IConfigurationRoot configurationRoot, WorkerManager workerCollection)
    {
        _logger = logger;
        _config = config;
        _configurationRoot = configurationRoot;
        _workerCollection = workerCollection;
    }

    [HttpGet("List")]
    public ActionResult<WorkerListResults> List() // List the workers (and perhaps current execution status, maybe also health status and retry count?)
    {
        bool success = true;
        List<WorkerListResult> workerListResultList = [];
        try
        {
            foreach (KeyValuePair<string, Worker> workerKVPair in _workerCollection.Workers)
            {
                Worker worker = workerKVPair.Value;
                WorkerListResult workerListResult = new()
                {
                    Name = worker.Name,
                    Script = worker.Config.Script,
                    IsExecuting = worker.IsExecuting,
                    LastExecution = worker.LastExecution,
                    LastSuccessfulExecution = worker.LastSuccessfulExecution,
                    HealthStatus = worker.HealthCheck().Status.ToString()
                };
                workerListResultList.Add(workerListResult);
            }
        }
        catch (Exception ex)
        {
            success = false;
            _logger.LogError("Unable to list workers due to exception: {ex.Message}", [ex.Message]);
            HttpContext.RaiseError(ex);
        }
        WorkerListResults workerListResults = new()
        {
            Workers = workerListResultList,
            Success = success
        };
        return workerListResults;
    }

    [HttpGet("TriggerUpdate")]
    public ActionResult<WorkerTriggerUpdateResult> TriggerUpdate(string name)
    {
        _workerCollection.Workers.TryGetValue(name, out Worker? worker);
        if (worker is null)
        {
            _logger.LogError("Unable to trigger worker {name} - no running worker with this name.", [name]);
            return new WorkerTriggerUpdateResult { Success = false };
        }
        _logger.LogInformation("triggering worker {name}.", [name]);
        ManualTriggerCallbackInfos callbackInfos = new();
        lock (worker.Scriptable)
        {
            worker.IsExecuting = true;
            worker.Scriptable.Update(callbackInfos);
            worker.IsExecuting = false;

            DateTime beforeExecution = DateTime.Now;
            worker.IsExecuting = true;
            try
            {
                worker.Scriptable.Update(callbackInfos);
            }
            finally
            {
                worker.IsExecuting = false;
                worker.LastExecution = beforeExecution;
            }
            DateTime afterExecution = DateTime.Now;
            WorkerManager.UpdateWorkerTimestamps(worker, beforeExecution, afterExecution);
        }
        _logger.LogInformation("triggered worker {name}.", [name]);
        return new WorkerTriggerUpdateResult { Success = true };

    }

}

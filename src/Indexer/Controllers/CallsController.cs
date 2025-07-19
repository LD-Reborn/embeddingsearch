using ElmahCore;
using Microsoft.AspNetCore.Mvc;
using Indexer.Models;

namespace Indexer.Controllers;

[ApiController]
[Route("[controller]")]
public class CallsController : ControllerBase
{
    private readonly ILogger<WorkerController> _logger;
    private readonly IConfiguration _config;
    private readonly IConfigurationRoot _configurationRoot;
    private readonly WorkerCollection _workerCollection;

    public CallsController(ILogger<WorkerController> logger, IConfiguration config, IConfigurationRoot configurationRoot, WorkerCollection workerCollection)
    {
        _logger = logger;
        _config = config;
        _configurationRoot = configurationRoot;
        _workerCollection = workerCollection;
    }

    [HttpGet("List")]
    public ActionResult<CallListResults> List(string name)
    {
        bool success = true;
        List<CallListResult> calls = [];
        var configWorkerSection = _config.GetSection("EmbeddingsearchIndexer:Worker");
        _workerCollection.Workers.TryGetValue(name, out Worker? worker);
        if (worker is null)
        {
            success = false;
            _logger.LogError("No worker found under the name {name}.", [name]);
            HttpContext.RaiseError(new Exception($"No worker found under the name {name}"));
        }
        else
        {
            foreach (ICall call in worker.Calls)
            {
                CallListResult callListResult = new()
                {
                    CallConfig = call.CallConfig,
                    IsActive = call.IsEnabled,
                    IsExecuting = call.IsExecuting,
                    LastExecution = call.LastExecution,
                    LastSuccessfulExecution = call.LastSuccessfulExecution,
                    HealthStatus = call.HealthCheck().Status.ToString()
                };
                calls.Add(callListResult);
            }
        }
        return new CallListResults() { Calls = calls, Success = success };
    }

    [HttpGet("Enable")]
    public ActionResult<WorkerStartResult> Enable(string name)
    {
        _workerCollection.Workers.TryGetValue(name, out Worker? worker);
        if (worker is null)
        {
            _logger.LogError("Unable to start calls in worker {name} - no running worker with this name.", [name]);
            return new WorkerStartResult { Success = false };
        }
        _logger.LogInformation("Starting calls in worker {name}.", [name]);
        foreach (ICall call in worker.Calls)
        {
            call.Start();
        }
        _logger.LogInformation("Starting calls in worker {name}.", [name]);
        return new WorkerStartResult { Success = true };
    }

    [HttpGet("Disable")]
    public ActionResult<WorkerStopResult> Disable(string name)
    {
        _workerCollection.Workers.TryGetValue(name, out Worker? worker);
        if (worker is null)
        {
            _logger.LogError("Unable to stop calls in worker {name} - no running worker with this name.", [name]);
            return new WorkerStopResult { Success = false };
        }
        _logger.LogInformation("Stopping calls in worker {name}.", [name]);
        foreach (ICall call in worker.Calls)
        {
            call.Stop();
        }
        _logger.LogInformation("Stopped calls in worker {name}.", [name]);
        return new WorkerStopResult { Success = true };
    }

    [HttpGet("Reload")]
    public ActionResult<WorkerReloadConfigResult> Reload()
    {
        try
        {
            _logger.LogInformation("Reloading configuration");
            _configurationRoot.Reload();
            _logger.LogInformation("Reloaded configuration");
            _logger.LogInformation("Destroying workers");
            foreach (KeyValuePair<string, Worker> workerKVPair in _workerCollection.Workers)
            {
                Worker worker = workerKVPair.Value;
                foreach (ICall call in worker.Calls)
                {
                    call.Stop();
                }
                _workerCollection.Workers.Remove(workerKVPair.Key);
                _logger.LogInformation("Destroyed worker {workerKVPair.Key}", [workerKVPair.Key]);
            }
            _logger.LogInformation("Destroyed workers");
            _workerCollection.InitializeWorkers();
            return new WorkerReloadConfigResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError("Exception {ex.Message} happened while trying to reload the worker configuration.", [ex.Message]);
            HttpContext.RaiseError(ex);
            return new WorkerReloadConfigResult { Success = false };
        }
    }

}

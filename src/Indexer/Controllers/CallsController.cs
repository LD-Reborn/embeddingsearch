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
    private readonly WorkerManager _workerCollection;

    public CallsController(ILogger<WorkerController> logger, IConfiguration config, IConfigurationRoot configurationRoot, WorkerManager workerCollection)
    {
        _logger = logger;
        _config = config;
        _configurationRoot = configurationRoot;
        _workerCollection = workerCollection;
    }

    [HttpGet("List")]
    public ActionResult<CallListResults> List(string workerName)
    {
        bool success = true;
        List<CallListResult> calls = [];
        var configWorkerSection = _config.GetSection("EmbeddingsearchIndexer:Worker");
        _workerCollection.Workers.TryGetValue(workerName, out Worker? worker);
        if (worker is null)
        {
            success = false;
            _logger.LogError("No worker found under the name {name}.", [workerName]);
            HttpContext.RaiseError(new Exception($"No worker found under the name {workerName}"));
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
    public ActionResult<CallEnableResult> Enable(string workerName, string? callName)
    {
        _workerCollection.Workers.TryGetValue(workerName, out Worker? worker);
        if (worker is null)
        {
            _logger.LogError("Unable to start calls in worker {workerName} - no running worker with this name.", [workerName]);
            return new CallEnableResult { Success = false };
        }
        if (callName is null)
        {
            if (worker.ScriptContainer.ToolSet.CancellationToken.IsCancellationRequested)
            {
                worker.CancellationTokenSource.Dispose();
                worker.CancellationTokenSource = new CancellationTokenSource();
                worker.ScriptContainer.ToolSet.CancellationToken = worker.CancellationTokenSource.Token;
            }
            _logger.LogInformation("Starting calls in worker {workerName}.", [workerName]);
            foreach (ICall call in worker.Calls)
            {
                call.Enable();
            }
            _logger.LogInformation("Finished starting calls in worker {workerName}.", [workerName]);
        }
        else
        {
            _logger.LogCritical(worker.Calls.First().Name);
            ICall? call = worker.Calls.Where(x => x.Name == callName).SingleOrDefault();
            if (call is null)
            {
                _logger.LogError("Unable to start call {callName} in worker {workerName} - no call with this name.", [callName, workerName]);
                return new CallEnableResult { Success = false };
            }
            _logger.LogInformation("Starting call {callName} in worker {workerName}.", [callName, workerName]);
            if (worker.ScriptContainer.ToolSet.CancellationToken.IsCancellationRequested)
            {
                worker.CancellationTokenSource.Dispose();
                worker.CancellationTokenSource = new CancellationTokenSource();
                worker.ScriptContainer.ToolSet.CancellationToken = worker.CancellationTokenSource.Token;
            }
            call.Enable();
        }
        return new CallEnableResult { Success = true };
    }

    [HttpGet("Disable")]
    public ActionResult<CallDisableResult> Disable(string workerName, string? callName, bool? requestStop = false)
    {
        _workerCollection.Workers.TryGetValue(workerName, out Worker? worker);
        if (worker is null)
        {
            _logger.LogError("Unable to stop calls in worker {name} - no running worker with this name.", [workerName]);
            return new CallDisableResult { Success = false };
        }
        if (callName is null)
        {
            _logger.LogInformation("Stopping calls in worker {name}.", [workerName]);
            foreach (ICall call in worker.Calls)
            {
                call.Disable();
                if (requestStop == true)
                {
                    call.Stop();
                }
            }
            _logger.LogInformation("Stopped calls in worker {name}.", [workerName]);
        } else
        {
            _logger.LogCritical(worker.Calls.First().Name);
            ICall? call = worker.Calls.Where(x => x.Name == callName).SingleOrDefault();
            if (call is null)
            {
                _logger.LogError("Unable to start call {callName} in worker {workerName} - no call with this name.", [callName, workerName]);
                return new CallDisableResult { Success = false };
            }
            _logger.LogInformation("Starting call {callName} in worker {workerName}.", [callName, workerName]);
            call.Disable();            
            if (requestStop == true)
            {
                call.Stop();
            }
        }
        return new CallDisableResult { Success = true };
    }
}

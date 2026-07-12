using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Indexer.Models;
using Indexer.Services;

namespace Indexer.Controllers;

[Authorize]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("[controller]")]
public class DashboardController : Controller
{
    private readonly ILogger<DashboardController> _logger;
    private readonly WorkerManager _workerManager;
    private readonly LogBroadcaster _logBroadcaster;

    public DashboardController(ILogger<DashboardController> logger, WorkerManager workerManager, LogBroadcaster logBroadcaster)
    {
        _logger = logger;
        _workerManager = workerManager;
        _logBroadcaster = logBroadcaster;
    }

    [HttpGet("/")]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet("workers")]
    public ActionResult<WorkerListResults> GetWorkers()
    {
        List<WorkerListResult> workerListResultList = [];
        foreach (KeyValuePair<string, Worker> workerKVPair in _workerManager.Workers)
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

            // Add calls info
            List<CallListResult> callResults = [];
            foreach (ICall call in worker.Calls)
            {
                callResults.Add(new CallListResult
                {
                    CallConfig = call.CallConfig,
                    IsActive = call.IsEnabled,
                    IsExecuting = call.IsExecuting,
                    LastExecution = call.LastExecution,
                    LastSuccessfulExecution = call.LastSuccessfulExecution,
                    HealthStatus = call.HealthCheck().Status.ToString()
                });
            }
            workerListResult.Calls = callResults;

            workerListResultList.Add(workerListResult);
        }

        return new WorkerListResults { Workers = workerListResultList, Success = true };
    }

    [HttpPost("trigger")]
    public ActionResult<WorkerTriggerUpdateResult> TriggerWorker([FromForm] string name)
    {
        if (!_workerManager.Workers.TryGetValue(name, out Worker? worker))
        {
            return new WorkerTriggerUpdateResult { Success = false };
        }

        // Queue the trigger asynchronously without blocking
        _ = Task.Run(() =>
        {
            ManualTriggerCallbackInfos callbackInfos = new();
            lock (worker.ScriptContainer)
            {
                worker.IsExecuting = true;
                try
                {
                    worker.ScriptContainer.Update(callbackInfos);
                    DateTime beforeExecution = DateTime.Now;
                    worker.IsExecuting = true;
                    worker.ScriptContainer.Update(callbackInfos);
                    worker.LastExecution = beforeExecution;
                }
                finally
                {
                    worker.IsExecuting = false;
                }
                WorkerManager.UpdateWorkerTimestamps(worker, DateTime.Now, DateTime.Now);
            }
        });

        // Return immediately
        return new WorkerTriggerUpdateResult { Success = true };
    }

    [HttpPost("enable")]
    public ActionResult<CallEnableResult> Enable([FromForm] string workerName, [FromForm] string? callName = null)
    {
        if (!_workerManager.Workers.TryGetValue(workerName, out Worker? worker))
            return new CallEnableResult { Success = false };

        if (callName is null)
        {
            foreach (ICall call in worker.Calls)
                call.Enable();
        }
        else
        {
            ICall? call = worker.Calls.Where(x => x.Name == callName).SingleOrDefault();
            if (call is null)
                return new CallEnableResult { Success = false };
            call.Enable();
        }
        return new CallEnableResult { Success = true };
    }

    [HttpPost("disable")]
    public ActionResult<CallDisableResult> Disable([FromForm] string workerName, [FromForm] string? callName = null, [FromForm] bool requestStop = false)
    {
        if (!_workerManager.Workers.TryGetValue(workerName, out Worker? worker))
            return new CallDisableResult { Success = false };

        if (callName is null)
        {
            foreach (ICall call in worker.Calls)
            {
                call.Disable();
                if (requestStop) call.Stop();
            }
        }
        else
        {
            ICall? call = worker.Calls.Where(x => x.Name == callName).SingleOrDefault();
            if (call is null)
                return new CallDisableResult { Success = false };
            call.Disable();
            if (requestStop) call.Stop();
        }
        return new CallDisableResult { Success = true };
    }

    [HttpPost("logs")]
    public ActionResult<WorkerLogsResults> GetLogs([FromForm] string workerName, [FromForm] int skip = 0, [FromForm] int count = 50)
    {
        if (!_workerManager.Workers.TryGetValue(workerName, out Worker? worker))
            return new WorkerLogsResults { WorkerName = workerName, Logs = [], Success = false };

        count = Math.Clamp(count, 1, 1000);
        List<LogEntry> logs = worker.GetRecentLogs(skip + count);
        List<LogEntry> paginatedLogs = [.. logs.Skip(skip).Take(count)];

        return new WorkerLogsResults { WorkerName = workerName, Logs = paginatedLogs, Success = true };
    }

    [HttpGet("logcounts")]
    public ActionResult<WorkerLogCountsResults> GetLogCounts()
    {
        Dictionary<string, Dictionary<string, int>> counts = new();
        foreach (KeyValuePair<string, Worker> kv in _workerManager.Workers)
        {
            counts[kv.Key] = kv.Value.GetLogCounts();
        }
        return new WorkerLogCountsResults { Success = true, Counts = counts };
    }

    [HttpGet("logs/stream")]
    public async Task GetLogStream([FromQuery] string workerName, CancellationToken cancellationToken)
    {
        if (!_workerManager.Workers.ContainsKey(workerName))
        {
            Response.StatusCode = 404;
            return;
        }

        int lastEventId = 0;
        if (Request.Headers.TryGetValue("Last-Event-ID", out var lastIdHeader)
            && int.TryParse(lastIdHeader.ToString(), out int parsed))
        {
            lastEventId = parsed;
        }

        LogBroadcaster.LogSubscription sub = _logBroadcaster.Subscribe(workerName, lastEventId);

        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (LogEntry log in sub.Channel.Reader.ReadAllAsync(cancellationToken))
            {
                string json = JsonSerializer.Serialize(log, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });
                await Response.WriteAsync($"id: {log.SequenceId}\nevent: log\ndata: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        finally
        {
            _logBroadcaster.Unsubscribe(workerName, sub.SubscriptionId);
        }
    }
}

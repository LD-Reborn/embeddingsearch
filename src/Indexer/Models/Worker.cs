using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Indexer.Exceptions;

namespace Indexer.Models;

public class WorkerCollection
{
    public Dictionary<string, Worker> Workers;
    public List<Type> types;
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private readonly Client.Client client;

    public WorkerCollection(ILogger<WorkerCollection> logger, IConfiguration configuration, Client.Client client)
    {
        Workers = [];
        types = [typeof(PythonScriptable)];
        _logger = logger;
        _configuration = configuration;
        this.client = client;
    }

    public void InitializeWorkers()
    {
        _logger.LogInformation("Initializing workers");
        // Load and configure all workers
        var sectionMain = _configuration.GetSection("EmbeddingsearchIndexer");
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
                ScriptToolSet toolSet = new(workerConfig.Script, client);
                InitializeWorker(toolSet, workerConfig);
            }
        }
        else
        {
            _logger.LogCritical("Unable to load section \"Worker\"");
            throw new IndexerConfigurationException("Unable to load section \"Worker\"");
        }
        _logger.LogInformation("Initialized workers");
    }

    public void InitializeWorker(ScriptToolSet toolSet, WorkerConfig workerConfig)
    {
        _logger.LogInformation("Initializing worker: {Name}", workerConfig.Name);
        Worker worker = new(workerConfig.Name, workerConfig, GetScriptable(toolSet));
        Workers[workerConfig.Name] = worker;
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
                    timer.AutoReset = true;
                    timer.Enabled = true;
                    DateTime now = DateTime.Now;
                    IntervalCall call = new(timer, worker.Scriptable, _logger, callConfig)
                    {
                        LastExecution = now,
                        LastSuccessfulExecution = now
                    };
                    timer.Elapsed += (sender, e) =>
                    {
                        try
                        {
                            call.LastExecution = DateTime.Now;
                            worker.Scriptable.Update(new IntervalCallbackInfos() { sender = sender, e = e });
                            call.LastSuccessfulExecution = DateTime.Now;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Exception occurred in a Call of Worker \"{name}\": \"{ex}\"", worker.Name, ex.Message);
                        }
                    };
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

    public IScriptable GetScriptable(ScriptToolSet toolSet)
    {
        string fileName = toolSet.filePath;
        foreach (Type type in types)
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
}

public class Worker
{
    public string Name { get; set; }
    public WorkerConfig Config { get; set; }
    public IScriptable Scriptable { get; set; }
    public List<ICall> Calls { get; set; }

    public Worker(string Name, WorkerConfig workerConfig, IScriptable scriptable)
    {
        this.Name = Name;
        this.Config = workerConfig;
        this.Scriptable = scriptable;
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

public class WorkerCollectionConfig
{
    public required List<WorkerConfig> Worker { get; set; }
}

public class WorkerConfig
{
    public required string Name { get; set; }
    public required List<string> Searchdomains { get; set; }
    public required string Script { get; set; }
    public required List<CallConfig> Calls { get; set; }
}

public class CallConfig
{
    public required string Type { get; set; }
    public long? Interval { get; set; } // For Type: Interval
    public string? Path { get; set; } // For Type: FileSystemWatcher
}

public interface ICall
{
    public HealthCheckResult HealthCheck();
    public void Start();
    public void Stop();
    public bool IsRunning { get; set; }
    public CallConfig CallConfig { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }
}

public class IntervalCall : ICall
{
    public System.Timers.Timer Timer;
    public IScriptable Scriptable;
    public ILogger _logger;
    public bool IsRunning { get; set; }
    public CallConfig CallConfig { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }

    public IntervalCall(System.Timers.Timer timer, IScriptable scriptable, ILogger logger, CallConfig callConfig)
    {
        Timer = timer;
        Scriptable = scriptable;
        _logger = logger;
        CallConfig = callConfig;
        IsRunning = true;
    }

    public void Start()
    {
        Timer.Start();
        IsRunning = true;
    }

    public void Stop()
    {
        Scriptable.Stop();
        Timer.Stop();
        IsRunning = false;
    }

    public HealthCheckResult HealthCheck()
    {
        if (!Scriptable.UpdateInfo.Successful)
        {
            _logger.LogWarning("HealthCheck revealed: The last execution of \"{name}\" was not successful", Scriptable.ToolSet.filePath);
            return HealthCheckResult.Unhealthy();
        }
        double timerInterval = Timer.Interval; // In ms
        DateTime lastRunDateTime = Scriptable.UpdateInfo.DateTime;
        DateTime now = DateTime.Now;
        double millisecondsSinceLastExecution = now.Subtract(lastRunDateTime).TotalMilliseconds;
        if (millisecondsSinceLastExecution >= 2 * timerInterval)
        {
            _logger.LogWarning("HealthCheck revealed: Since the last execution of \"{name}\" more than twice the interval has passed", Scriptable.ToolSet.filePath);
            return HealthCheckResult.Unhealthy();
        }
        return HealthCheckResult.Healthy();
    }

}

public class ScheduleCall : ICall
{
    public bool IsRunning { get; set; }
    public CallConfig CallConfig { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }

    public ScheduleCall(CallConfig callConfig)
    {
        CallConfig = callConfig;
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public HealthCheckResult HealthCheck()
    {
        return HealthCheckResult.Unhealthy(); // Not implemented yet
    }
}

public class FileUpdateCall : ICall
{
    public bool IsRunning { get; set; }
    public CallConfig CallConfig { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }

    public FileUpdateCall(CallConfig callConfig)
    {
        CallConfig = callConfig;
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public HealthCheckResult HealthCheck()
    {
        return HealthCheckResult.Unhealthy(); // Not implemented yet
    }
}
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Indexer.Exceptions;
using Quartz;
using Quartz.Impl;

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
                case "runonce":
                    RunOnceCall runOnceCall = new(worker, _logger, callConfig);
                    worker.Calls.Add(runOnceCall);
                    break;
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
                    IntervalCall intervallCall = new(timer, worker.Scriptable, _logger, callConfig)
                    {
                        LastExecution = now,
                        LastSuccessfulExecution = now
                    };
                    timer.Elapsed += (sender, e) =>
                    {
                        try
                        {
                            DateTime beforeExecution = DateTime.Now;
                            intervallCall.IsExecuting = true;
                            try
                            {
                                worker.Scriptable.Update(new IntervalCallbackInfos() { sender = sender, e = e });
                            }
                            finally
                            {
                                intervallCall.IsExecuting = false;
                                intervallCall.LastExecution = beforeExecution;
                                worker.LastExecution = beforeExecution;
                            }
                            DateTime afterExecution = DateTime.Now;
                            UpdateCallAndWorkerTimestamps(intervallCall, worker, beforeExecution, afterExecution);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Exception occurred in a Call of Worker \"{name}\": \"{ex}\"", worker.Name, ex.Message);
                        }
                    };
                    worker.Calls.Add(intervallCall);
                    break;
                case "schedule": // TODO implement scheduled tasks using Quartz
                    ScheduleCall scheduleCall = new(worker, callConfig, _logger);
                    worker.Calls.Add(scheduleCall);
                    break;
                case "fileupdate":
                    if (callConfig.Path is null)
                    {
                        _logger.LogError("Path not set for a Call in Worker \"{Name}\"", workerConfig.Name);
                        throw new IndexerConfigurationException($"Path not set for a Call in Worker \"{workerConfig.Name}\"");
                    }
                    FileUpdateCall fileUpdateCall = new(worker, callConfig, _logger);
                    worker.Calls.Add(fileUpdateCall);
                    break;
                default:
                    throw new IndexerConfigurationException($"Unknown Type specified for a Call in Worker \"{workerConfig.Name}\"");
            }
        }
    }

    public static void UpdateCallAndWorkerTimestamps(ICall call, Worker worker, DateTime beforeExecution, DateTime afterExecution)
    {
        UpdateCallTimestamps(call, beforeExecution, afterExecution);
        UpdateWorkerTimestamps(worker, beforeExecution, afterExecution);
    }

    public static void UpdateCallTimestamps(ICall call, DateTime beforeExecution, DateTime afterExecution)
    {
        call.LastSuccessfulExecution = GetNewestDateTime(call.LastSuccessfulExecution, afterExecution);        
    }

    public static void UpdateWorkerTimestamps(Worker worker, DateTime beforeExecution, DateTime afterExecution)
    {
        worker.LastSuccessfulExecution = GetNewestDateTime(worker.LastSuccessfulExecution, afterExecution);
    }


    public static DateTime? GetNewestDateTime(DateTime? preexistingDateTime, DateTime incomingDateTime)
    {
        if (preexistingDateTime is null || preexistingDateTime.Value.CompareTo(incomingDateTime) < 0)
        {
            return incomingDateTime;
        }
        return preexistingDateTime;
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
    public bool IsExecuting { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }

    public Worker(string name, WorkerConfig workerConfig, IScriptable scriptable)
    {
        Name = name;
        Config = workerConfig;
        Scriptable = scriptable;
        IsExecuting = false;
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
    public string? Schedule { get; set; } // For Type: Schedule
}

public interface ICall
{
    public HealthCheckResult HealthCheck();
    public void Start();
    public void Stop();
    public bool IsEnabled { get; set; }
    public bool IsExecuting { get; set; }
    public CallConfig CallConfig { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }
}

public class RunOnceCall : ICall
{
    public ILogger _logger;
    public bool IsEnabled { get; set; }
    public bool IsExecuting { get; set; }
    public Worker Worker { get; }
    public CallConfig CallConfig { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }

    public RunOnceCall(Worker worker, ILogger logger, CallConfig callConfig)
    {
        Worker = worker;
        _logger = logger;
        CallConfig = callConfig;
        IsEnabled = true;
        IsExecuting = false;
        IndexAsync();
    }

    public void Start()
    {
        IndexAsync();
        IsEnabled = true;
    }

    public void Stop()
    {
        IsEnabled = false;
    }

    private async void IndexAsync()
    {
        try
        {
            DateTime beforeExecution = DateTime.Now;
            IsExecuting = true;
            try
            {
                await Task.Run(() => Worker.Scriptable.Update(new RunOnceCallbackInfos()));
            }
            finally
            {
                IsExecuting = false;
                LastExecution = beforeExecution;
                Worker.LastExecution = beforeExecution;
            }
            DateTime afterExecution = DateTime.Now;
            WorkerCollection.UpdateCallAndWorkerTimestamps(this, Worker, beforeExecution, afterExecution);
        }
        catch (Exception ex)
        {
            _logger.LogError("Exception occurred in a Call of Worker \"{name}\": \"{ex}\"", Worker.Name, ex.Message);
        }
    }

    public HealthCheckResult HealthCheck()
    {
        return HealthCheckResult.Healthy(); // TODO implement proper healthcheck
    }

}

public class IntervalCall : ICall
{
    public System.Timers.Timer Timer;
    public IScriptable Scriptable;
    public ILogger _logger;
    public bool IsEnabled { get; set; }
    public bool IsExecuting { get; set; }
    public CallConfig CallConfig { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }

    public IntervalCall(System.Timers.Timer timer, IScriptable scriptable, ILogger logger, CallConfig callConfig)
    {
        Timer = timer;
        Scriptable = scriptable;
        _logger = logger;
        CallConfig = callConfig;
        IsEnabled = true;
        IsExecuting = false;
    }

    public void Start()
    {
        Timer.Start();
        IsEnabled = true;
    }

    public void Stop()
    {
        Scriptable.Stop();
        Timer.Stop();
        IsEnabled = false;
    }

    public HealthCheckResult HealthCheck()
    {
        if (!Scriptable.UpdateInfo.Successful)
        {
            _logger.LogWarning("HealthCheck revealed: The last execution of \"{name}\" was not successful", Scriptable.ToolSet.filePath);
            return HealthCheckResult.Unhealthy($"HealthCheck revealed: The last execution of \"{Scriptable.ToolSet.filePath}\" was not successful");
        }
        double timerInterval = Timer.Interval; // In ms
        DateTime lastRunDateTime = Scriptable.UpdateInfo.DateTime;
        DateTime now = DateTime.Now;
        double millisecondsSinceLastExecution = now.Subtract(lastRunDateTime).TotalMilliseconds;
        if (millisecondsSinceLastExecution >= 2 * timerInterval)
        {
            _logger.LogWarning("HealthCheck revealed: Since the last execution of \"{name}\" more than twice the interval has passed", Scriptable.ToolSet.filePath);
            return HealthCheckResult.Unhealthy($"HealthCheck revealed: Since the last execution of \"{Scriptable.ToolSet.filePath}\" more than twice the interval has passed");
        }
        return HealthCheckResult.Healthy();
    }

}

public class ScheduleCall : ICall
{
    public bool IsEnabled { get; set; }
    public bool IsExecuting { get; set; }
    public Worker Worker { get; }
    public JobKey JobKey { get; }
    public JobDataMap JobDataMap { get; }
    public CallConfig CallConfig { get; set; }
    private ILogger _logger { get; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }
    private StdSchedulerFactory SchedulerFactory { get; }
    private IScheduler Scheduler { get; }

    public ScheduleCall(Worker worker, CallConfig callConfig, ILogger logger)
    {
        Worker = worker;
        CallConfig = callConfig;
        _logger = logger;
        IsEnabled = false;
        IsExecuting = false;
        JobKey = new(worker.Name);
        SchedulerFactory = new();
        Scheduler = SchedulerFactory.GetScheduler(CancellationToken.None).Result;
        JobDataMap = [];
        JobDataMap["action"] = () =>
        {
            try
            {
                DateTime beforeExecution = DateTime.Now;
                IsExecuting = true;
                try
                {
                    worker.Scriptable.Update(new ScheduleCallbackInfos());
                }
                finally
                {
                    IsExecuting = false;
                    LastExecution = beforeExecution;
                    worker.LastExecution = beforeExecution;
                }
                DateTime afterExecution = DateTime.Now;
                WorkerCollection.UpdateCallAndWorkerTimestamps(this, worker, beforeExecution, afterExecution);
            }
            catch (Exception ex)
            {
                _logger.LogError("Exception occurred in a Call of Worker \"{name}\": \"{ex}\"", worker.Name, ex.Message);
            }
        };
        CreateJob().Wait();
        Start();
    }

    public void Start()
    {
        if (!IsEnabled)
        {
            Scheduler.Start(CancellationToken.None).Wait();
            IsEnabled = true;
        }
    }

    public void Stop()
    {
        Scheduler.PauseAll();
        IsEnabled = false;
    }


    private async Task CreateJob()
    {
        if (CallConfig.Schedule is null)
        {
            throw new IndexerConfigurationException($"Interval not set for a Call in Worker \"{Worker.Name}\"");
        }
        try
        {

            await Scheduler.ScheduleJob(
                JobBuilder.Create<ActionJob>()
                .WithIdentity(JobKey)
                .Build(),
                TriggerBuilder.Create()
                .ForJob(JobKey)
                .WithIdentity(Worker.Name + "-trigger")
                .UsingJobData(JobDataMap)
                .WithCronSchedule(CallConfig.Schedule)
                .Build(),
                CancellationToken.None);
        }
        catch (FormatException)
        {
            throw new IndexerConfigurationException($"Quartz Cron expression invalid in Worker \"{Worker.Name}\" - Quartz syntax differs from classic cron");
        }
    }

    public HealthCheckResult HealthCheck()
    {
        return HealthCheckResult.Unhealthy(); // Not implemented yet
    }
}

public class FileUpdateCall : ICall
{
    public bool IsEnabled { get; set; }
    public bool IsExecuting { get; set; }
    public Worker Worker { get; }
    public CallConfig CallConfig { get; set; }
    private ILogger _logger { get; }
    private FileSystemWatcher _watcher { get; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }

    public FileUpdateCall(Worker worker, CallConfig callConfig, ILogger logger)
    {
        Worker = worker;
        CallConfig = callConfig;
        _logger = logger;
        IsEnabled = true;
        IsExecuting = false;
        if (CallConfig.Path is null)
        {
            throw new IndexerConfigurationException($"Path not set for a Call in Worker \"{Worker.Name}\"");
        }
        _watcher = new FileSystemWatcher(CallConfig.Path);
        _watcher.Changed += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    public void Start()
    {
        if (!IsEnabled)
        {
            IsEnabled = true;
            _watcher.EnableRaisingEvents = true;
            Index();
        }
    }

    public void Stop()
    {
        if (IsEnabled)
        {
            IsEnabled = false;
            _watcher.EnableRaisingEvents = false;
        }
    }


    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }
        Index(sender, e);
    }

    private void Index(object? sender, FileSystemEventArgs? e)
    {
        try
        {
            DateTime beforeExecution = DateTime.Now;
            IsExecuting = true;
            try
            {
                Worker.Scriptable.Update(new FileUpdateCallbackInfos() {sender = sender, e = e});
            }
            finally
            {
                IsExecuting = false;
                LastExecution = beforeExecution;
                Worker.LastExecution = beforeExecution;
            }
            DateTime afterExecution = DateTime.Now;
            WorkerCollection.UpdateCallAndWorkerTimestamps(this, Worker, beforeExecution, afterExecution);
        }
        catch (Exception ex)
        {
            _logger.LogError("Exception occurred in a Call of Worker \"{name}\": \"{ex}\"", Worker.Name, ex.Message);
        }
    }

    private void Index()
    {
        Index(null, null);
    }

    public HealthCheckResult HealthCheck()
    {
        return HealthCheckResult.Unhealthy(); // Not implemented yet
    }
}
using Indexer.Scriptables;
using Indexer.Exceptions;
using Indexer.Models;

public class WorkerManager
{
    public Dictionary<string, Worker> Workers;
    public List<Type> types;
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private readonly Client.Client client;

    public WorkerManager(ILogger<WorkerManager> logger, IConfiguration configuration, Client.Client client)
    {
        Workers = [];
        types = [typeof(PythonScriptable), typeof(CSharpScriptable)];
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
                ScriptToolSet toolSet = new(workerConfig.Script, client, _logger, _configuration, workerConfig.Name);
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
        string fileName = toolSet.FilePath ?? throw new IndexerConfigurationException($"\"Script\" not set for Worker \"{toolSet.Name}\"");
        foreach (Type type in types)
        {
            System.Reflection.MethodInfo? method = type.GetMethod("IsScript");
            bool? isInstance = method is not null ? (bool?)method.Invoke(null, [fileName]) : null;
            if (isInstance == true)
            {
                IScriptable? instance = (IScriptable?)Activator.CreateInstance(type, [toolSet, _logger]);
                if (instance is null)
                {
                    _logger.LogError("Unable to initialize script: \"{fileName}\"", fileName);
                    throw new Exception($"Unable to initialize script: \"{fileName}\"");
                }
                return instance;
            }
        }
        _logger.LogError("Unable to determine the script's language: \"{fileName}\"", fileName);
        throw new UnknownScriptLanguageException(fileName);
    }
}

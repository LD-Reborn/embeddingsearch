using Indexer.Exceptions;
using Indexer.Models;
using Indexer.ScriptContainers;
using Microsoft.Extensions.Options;

public class WorkerManager
{
    public Dictionary<string, Worker> Workers;
    public List<Type> types;
    private readonly ILogger<WorkerManager> _logger;
    private readonly IndexerOptions _configuration;
    private readonly Client.Client client;

    public WorkerManager(ILogger<WorkerManager> logger, IOptions<IndexerOptions> configuration, Client.Client client)
    {
        Workers = [];
        types = [typeof(PythonScriptable), typeof(CSharpScriptable)];
        _logger = logger;
        _configuration = configuration.Value;
        this.client = client;
    }

    public void InitializeWorkers()
    {
        _logger.LogInformation("Initializing workers");
        // Load and configure all workers

        foreach (WorkerConfig workerConfig in _configuration.Workers)
        {
            CancellationTokenSource cancellationTokenSource = new();
            ScriptToolSet toolSet = new(workerConfig.Script, client, _logger, _configuration, cancellationTokenSource.Token, workerConfig.Name);
            InitializeWorker(toolSet, workerConfig, cancellationTokenSource);
        }
        _logger.LogInformation("Initialized workers");
    }

    public void InitializeWorker(ScriptToolSet toolSet, WorkerConfig workerConfig, CancellationTokenSource cancellationTokenSource)
    {
        _logger.LogInformation("Initializing worker: {Name}", workerConfig.Name);
        Worker worker = new(workerConfig.Name, workerConfig, GetScriptable(toolSet), cancellationTokenSource);
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
                    IntervalCall intervallCall = new(worker, _logger, callConfig);
                    worker.Calls.Add(intervallCall);
                    break;
                case "schedule":
                    ScheduleCall scheduleCall = new(worker, callConfig, _logger);
                    worker.Calls.Add(scheduleCall);
                    break;
                case "fileupdate":
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

    public IScriptContainer GetScriptable(ScriptToolSet toolSet)
    {
        string fileName = toolSet.FilePath ?? throw new IndexerConfigurationException($"\"Script\" not set for Worker \"{toolSet.Name}\"");
        foreach (Type type in types)
        {
            System.Reflection.MethodInfo? method = type.GetMethod("IsScript");
            bool? isInstance = method is not null ? (bool?)method.Invoke(null, [fileName]) : null;
            if (isInstance == true)
            {
                IScriptContainer? instance = (IScriptContainer?)Activator.CreateInstance(type, [toolSet, _logger]);
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

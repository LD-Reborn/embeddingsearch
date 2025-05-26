namespace Indexer.Models;

public class WorkerCollection
{
    public List<Worker> Workers;
    public List<Type> types;
    public WorkerCollection()
    {
        Workers = [];
        types = [typeof(PythonScriptable)];
    }
}

public class Worker
{
    public WorkerConfig Config { get; set; }
    public IScriptable Scriptable { get; set; }

    public Worker(WorkerConfig workerConfig, IScriptable scriptable)
    {
        this.Config = workerConfig;
        this.Scriptable = scriptable;
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
    public required List<Call> Calls { get; set; }
}

public class Call
{
    public required string Type { get; set; }
    public long? Interval { get; set; } // For Type: Interval
    public string? Path { get; set; } // For Type: FileSystemWatcher
}


namespace Indexer.Models;

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

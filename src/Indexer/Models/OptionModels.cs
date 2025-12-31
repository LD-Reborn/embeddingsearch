using Shared.Models;
namespace Indexer.Models;

public class IndexerOptions : ApiKeyOptions
{
    public required WorkerConfig[] Workers { get; set; }
    public required ServerOptions Server { get; set;}
    public required string PythonRuntime { get; set; } = "libpython3.13.so";
}

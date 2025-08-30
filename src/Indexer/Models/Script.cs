using System.Timers;

namespace Indexer.Models;

public class ScriptToolSet
{
    public string FilePath;
    public Client.Client Client;
    public ILogger Logger;
    public ICallbackInfos? CallbackInfos;
    public IConfiguration Configuration;
    public string Name;

    public ScriptToolSet(string filePath, Client.Client client, ILogger logger, IConfiguration configuration, string name)
    {
        Configuration = configuration;
        Name = name;
        FilePath = filePath;
        Client = client;
        Logger = logger;
    }
}

public class RunOnceCallbackInfos : ICallbackInfos {}

public class IntervalCallbackInfos : ICallbackInfos
{
    public object? sender;
    public required ElapsedEventArgs e;

}

public class ScheduleCallbackInfos : ICallbackInfos {}

public class FileUpdateCallbackInfos : ICallbackInfos
{
    public required object? sender;
    public required FileSystemEventArgs? e;
}

public class ManualTriggerCallbackInfos : ICallbackInfos { }

public struct ScriptUpdateInfo
{
    public DateTime DateTime { get; set; }
    public bool Successful { get; set; }
    public Exception? Exception { get; set; }
}
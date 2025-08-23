using System.Timers;
using Python.Runtime;

namespace Indexer.Models;

public class PythonScriptable : IScriptable
{
    public ScriptToolSet ToolSet { get; set; }
    public PyObject? pyToolSet;
    public PyModule scope;
    public dynamic sys;
    public string source;
    public bool SourceLoaded { get; set; }
    public ScriptUpdateInfo UpdateInfo { get; set; }
    public ILogger _logger { get; set; }
    public PythonScriptable(ScriptToolSet toolSet, ILogger logger)
    {
        _logger = logger;
        SourceLoaded = false;
        Runtime.PythonDLL ??= @"libpython3.12.so";
        if (!PythonEngine.IsInitialized)
        {
            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();
        }
        ToolSet = toolSet;
        source = File.ReadAllText(ToolSet.filePath);
        string fullPath = Path.GetFullPath(ToolSet.filePath);
        string? scriptDir = Path.GetDirectoryName(fullPath);
        using (Py.GIL())
        {
            scope = Py.CreateScope();
            sys = Py.Import("sys");
            if (scriptDir is not null)
            {
                sys.path.append(scriptDir);
            }
        }
        Init();
    }

    public void Init()
    {
        ExecFunction("init");
    }

    public void Update(ICallbackInfos callbackInfos)
    {
        ExecFunction("update");
    }

    public void Stop()
    {
        ExecFunction("stop");
    }

    public void ExecFunction(string name, ICallbackInfos? callbackInfos = null)
    {
       int retryCounter = 0;
        retry:
        try
        {
            using (Py.GIL())
            {
                pyToolSet = ToolSet.ToPython();
                pyToolSet.SetAttr("callbackInfos", callbackInfos.ToPython());
                scope.Set("toolset", pyToolSet);
                if (!SourceLoaded)
                {
                    scope.Exec(source);
                    SourceLoaded = true;
                }
                scope.Exec($"{name}(toolset)");
            }
        }
        catch (Exception ex)
        {
            UpdateInfo = new() { DateTime = DateTime.Now, Successful = false, Exception = ex };
            if (retryCounter < 3)
            {
                _logger.LogWarning("Execution of {name} function in script {Toolset.filePath} failed to an exception {ex.Message}", [name, ToolSet.filePath, ex.Message]);
                retryCounter++;
                goto retry;
            }
            _logger.LogError("Execution of {name} function in script {Toolset.filePath} failed to an exception {ex.Message}", [name, ToolSet.filePath, ex.Message]);
        }
        UpdateInfo = new() { DateTime = DateTime.Now, Successful = true };
    }

    public bool IsScript(string fileName)
    {
        return fileName.EndsWith(".py");
    }
}

/*
    TODO Add the following languages
    - Javascript
    - Golang (reconsider)
*/

public class ScriptToolSet
{
    public string filePath;
    public Client.Client client;
    public ICallbackInfos? callbackInfos;

    // IConfiguration - Access to connection strings, ollama, etc. maybe?
    public ScriptToolSet(string filePath, Client.Client client)
    {
        this.filePath = filePath;
        this.client = client;
    }
}

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
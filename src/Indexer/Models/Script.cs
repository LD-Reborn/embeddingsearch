using System.Text.Json;
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
    public PythonScriptable(ScriptToolSet toolSet)
    {
        Runtime.PythonDLL = @"libpython3.12.so";
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
        using (Py.GIL())
        {
            pyToolSet = ToolSet.ToPython();
            scope.Set("toolset", pyToolSet);
            scope.Exec(source);
            scope.Exec("init(toolset)");
        }
    }

    public void Update(ICallbackInfos callbackInfos)
    {
        using (Py.GIL())
        {
            pyToolSet = ToolSet.ToPython();
            pyToolSet.SetAttr("callbackInfos", callbackInfos.ToPython());
            scope.Set("toolset", pyToolSet);
            scope.Exec("update(toolset)");
        }
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
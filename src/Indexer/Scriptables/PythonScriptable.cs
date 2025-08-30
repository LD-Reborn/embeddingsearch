using Python.Runtime;
using Indexer.Models;

namespace Indexer.Scriptables;

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
        string? runtime = toolSet.Configuration.GetValue<string>("EmbeddingsearchIndexer:PythonRuntime");
        if (runtime is not null)
        {
            Runtime.PythonDLL ??= runtime;
        }
        _logger = logger;
        SourceLoaded = false;
        if (!PythonEngine.IsInitialized)
        {
            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();
        }
        ToolSet = toolSet;
        source = File.ReadAllText(ToolSet.FilePath);
        string fullPath = Path.GetFullPath(ToolSet.FilePath);
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

    public int Init()
    {
        return ExecFunction("init");
    }

    public int Update(ICallbackInfos callbackInfos)
    {
        return ExecFunction("update");
    }

    public int Stop()
    {
        return ExecFunction("stop");
    }

    public int ExecFunction(string name, ICallbackInfos? callbackInfos = null)
    {
        int error = 0;
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
                _logger.LogWarning("Execution of {name} function in script {Toolset.filePath} failed to an exception {ex.Message}", [name, ToolSet.FilePath, ex.Message]);
                retryCounter++;
                goto retry;
            }
            _logger.LogError("Execution of {name} function in script {Toolset.filePath} failed to an exception {ex.Message}", [name, ToolSet.FilePath, ex.Message]);
            error = 1;
        }
        UpdateInfo = new() { DateTime = DateTime.Now, Successful = true };
        return error;
    }

    public static bool IsScript(string fileName)
    {
        return fileName.EndsWith(".py");
    }
}

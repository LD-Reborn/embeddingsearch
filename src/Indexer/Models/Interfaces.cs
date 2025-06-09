namespace Indexer.Models;

public interface IScriptable
{
    ScriptToolSet ToolSet { get; set; }
    ScriptUpdateInfo UpdateInfo { get; set; }
    ILogger Logger { get; set; }
    void Init();
    void Update(ICallbackInfos callbackInfos);
    bool IsScript(string filePath);
}

public interface ICallbackInfos { }


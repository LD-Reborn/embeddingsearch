namespace Indexer.Models;

public interface IScriptable
{
    ScriptToolSet ToolSet { get; set; }
    ScriptUpdateInfo UpdateInfo { get; set; }
    ILogger _logger { get; set; }
    void Init();
    void Update(ICallbackInfos callbackInfos);
    void Stop();

    bool IsScript(string filePath);
}

public interface ICallbackInfos { }


namespace Indexer.Models;

public interface IScriptable
{
    ScriptToolSet ToolSet { get; set; }
    ScriptUpdateInfo UpdateInfo { get; set; }
    ILogger _logger { get; set; }
    int Init();
    int Update(ICallbackInfos callbackInfos);
    int Stop();

    abstract static bool IsScript(string filePath);
}
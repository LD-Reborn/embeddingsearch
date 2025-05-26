namespace Indexer.Models;

public interface IScriptable
{
    ScriptToolSet ToolSet { get; set; }
    void Init();
    void Update(ICallbackInfos callbackInfos);
    bool IsScript(string filePath);
}

public interface ICallbackInfos { }


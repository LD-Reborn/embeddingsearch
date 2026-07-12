namespace Indexer.Models;

public interface IScriptContainer
{
    ScriptToolSet ToolSet { get; set; }
    ScriptUpdateInfo UpdateInfo { get; set; }
    ILogger _logger { get; set; }
    int Init();
    int Update(ICallbackInfos callbackInfos);
    int Stop();

    abstract static bool IsScript(string filePath);
}

public class LogEntry
{
    public int SequenceId { get; set; }
    public required string Message { get; set; }
    public object[]? Args { get; set; }
    public LogLevel LogLevel { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? WorkerCallId { get; set; }
}
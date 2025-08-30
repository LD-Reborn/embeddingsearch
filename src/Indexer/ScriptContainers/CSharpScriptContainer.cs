using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Indexer.Models;

namespace Indexer.ScriptContainers;

public class CSharpScriptable : IScriptable
{
    public ScriptToolSet ToolSet { get; set; }
    public ScriptUpdateInfo UpdateInfo { get; set; }
    public ILogger _logger { get; set; }
    public IScript script;
    public CSharpScriptable(ScriptToolSet toolSet, ILogger logger)
    {
        _logger = logger;
        ToolSet = toolSet;

        try
        {
            script = LoadScriptAsync(ToolSet).Result;
            Init();
        }
        catch (Exception ex)
        {
            _logger.LogCritical("Exception loading the script {ToolSet.filePath} CSharpScriptable {ex}", [ToolSet.FilePath, ex.StackTrace]);
            throw;
        }


    }

    public int Init()
    {
        return script.Init(ToolSet);
    }

    public int Update(ICallbackInfos callbackInfos)
    {
        return script.Update(callbackInfos);
    }

    public int Stop()
    {
        return script.Stop();
    }
    public async Task<IScript> LoadScriptAsync(ScriptToolSet toolSet)
    {
        string path = toolSet.FilePath;
        var fileText = File.ReadAllText(path);
        var code = string.Join("\n", fileText.Split("\n").Select(line => line.StartsWith("#load") ? "// " + line : line)); // CRUTCH! enables syntax highlighting via "#load" directive

        var options = ScriptOptions.Default
            .WithReferences(typeof(IScript).Assembly)
            .WithImports("System")
            .WithImports("System.Linq")
            .WithImports("System.Console")
            .WithImports("System.Collections")
            .WithImports("Indexer.Models");
        try
        {
            return await CSharpScript.EvaluateAsync<IScript>(code, options);
        }
        catch (Exception ex)
        {
            _logger.LogCritical("Exception loading the script {ToolSet.filePath} CSharpScriptable {ex.Message} {ex.StackTrace}", [ToolSet.FilePath, ex.Message, ex.StackTrace]);
            throw;
        }
    }

    public static bool IsScript(string fileName)
    {
        return fileName.EndsWith(".cs") || fileName.EndsWith(".csx");
    }
}

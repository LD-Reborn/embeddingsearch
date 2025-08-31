#load "../../Client/Client.cs"
#load "../Models/ScriptModels.cs"
#load "../Models/WorkerResultModels.cs"
#load "../../Shared/Models/SearchdomainResults.cs"
#load "../../Shared/Models/JSONModels.cs"
#load "../../Shared/Models/EntityResults.cs"

using Shared.Models;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

public class ExampleScript : Indexer.Models.IScript
{
    public Indexer.Models.ScriptToolSet ToolSet;
    public Client.Client client;
    string defaultSearchdomain;
    string exampleContent;
    string probMethod;
    string similarityMethod;
    string exampleSearchdomain;
    int exampleCounter;
    List<string> models;
    string probmethodDatapoint;
    string probmethodEntity;

    public ExampleScript()
    {
        //System.Console.WriteLine("DEBUG@example.cs - Constructor"); // logger not passed here yet
        exampleContent = "./Scripts/example_content";
        probMethod = "HVEWAvg";
        similarityMethod = "Cosine";
        exampleSearchdomain = "example_" + probMethod;
        exampleCounter = 0;
        models = ["ollama:bge-m3", "ollama:mxbai-embed-large"];
        probmethodDatapoint = probMethod;
        probmethodEntity = probMethod;
    }

    public int Init(Indexer.Models.ScriptToolSet toolSet)
    {
        ToolSet = toolSet;
        ToolSet.Logger.LogInformation("{ToolSet.Name} - Init", ToolSet.Name);
        SearchdomainListResults searchdomains = ToolSet.Client.SearchdomainListAsync().Result;
        defaultSearchdomain = searchdomains.Searchdomains.First();
        var searchdomainList = string.Join("\n", searchdomains.Searchdomains);
        ToolSet.Logger.LogInformation(searchdomainList);
        return 0;
    }

    public int Update(Indexer.Models.ICallbackInfos callbackInfos)
    {
        ToolSet.Logger.LogInformation("{ToolSet.Name} - Update", ToolSet.Name);
        EntityQueryResults test = ToolSet.Client.EntityQueryAsync(defaultSearchdomain, "DNA").Result;
        var firstResult = test.Results.ToArray()[0];
        ToolSet.Logger.LogInformation(firstResult.Name);
        ToolSet.Logger.LogInformation(firstResult.Value.ToString());
        return 0;
    }

    public int Stop()
    {
        ToolSet.Logger.LogInformation("DEBUG@example.csx - Stop");
        return 0;
    }
}

return new ExampleScript();
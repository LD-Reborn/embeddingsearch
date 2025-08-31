import os
from tools import *
import json
from dataclasses import asdict
import time

example_content = "./Scripts/example_content"
probmethod = "HVEWAvg"
similarityMethod = "Cosine"
example_searchdomain = "example_" + probmethod
example_counter = 0
models = ["ollama:bge-m3", "ollama:mxbai-embed-large"]
probmethod_datapoint = probmethod
probmethod_entity = probmethod
# Example for a dictionary based weighted average:
#   probmethod_datapoint = "DictionaryWeightedAverage:{\"ollama:bge-m3\": 4, \"ollama:mxbai-embed-large\": 1}"
#   probmethod_entity = "DictionaryWeightedAverage:{\"title\": 2, \"filename\": 0.1, \"text\": 0.25}"

def init(toolset: Toolset):
    global example_counter
    toolset.Logger.LogInformation("{toolset.Name} - init", toolset.Name)
    toolset.Logger.LogInformation("This is the init function from the python example script")
    toolset.Logger.LogInformation(f"example_counter: {example_counter}")
    searchdomainlist:SearchdomainListResults = toolset.Client.SearchdomainListAsync().Result
    if example_searchdomain not in searchdomainlist.Searchdomains:
        toolset.Client.SearchdomainCreateAsync(example_searchdomain).Result
        searchdomainlist = toolset.Client.SearchdomainListAsync().Result
    output = "Currently these searchdomains exist:\n"
    for searchdomain in searchdomainlist.Searchdomains:
        output += f" - {searchdomain}\n"
    toolset.Logger.LogInformation(output)

def update(toolset: Toolset):
    global example_counter
    toolset.Logger.LogInformation("{toolset.Name} - update", toolset.Name)    
    toolset.Logger.LogInformation("This is the update function from the python example script")
    callbackInfos:ICallbackInfos = toolset.CallbackInfos
    if (str(callbackInfos) == "Indexer.Models.RunOnceCallbackInfos"):
        toolset.Logger.LogInformation("It was triggered by a runonce call")
    elif (str(callbackInfos) == "Indexer.Models.IntervalCallbackInfos"):
        toolset.Logger.LogInformation("It was triggered by an interval call")
    elif (str(callbackInfos) == "Indexer.Models.ScheduleCallbackInfos"):
        toolset.Logger.LogInformation("It was triggered by a schedule call")
    elif (str(callbackInfos) == "Indexer.Models.FileUpdateCallbackInfos"):
        toolset.Logger.LogInformation("It was triggered by a fileupdate call")
    else:
        toolset.Logger.LogInformation("It was triggered, but the origin of the call could not be determined")
    example_counter += 1
    toolset.Logger.LogInformation(f"example_counter: {example_counter}")
    index_files(toolset)

def index_files(toolset: Toolset):
    jsonEntities:list = []
    for filename in os.listdir(example_content):
        qualified_filepath = example_content + "/" + filename
        with open(qualified_filepath, "r", encoding='utf-8', errors="replace") as file:
            title = file.readline()
            text = file.read()
        datapoints:list = [
            JSONDatapoint("filename", qualified_filepath, probmethod_datapoint, similarityMethod, models),
            JSONDatapoint("title", title, probmethod_datapoint, similarityMethod, models),
            JSONDatapoint("text", text, probmethod_datapoint, similarityMethod, models)
        ]
        jsonEntity:dict = asdict(JSONEntity(qualified_filepath, probmethod_entity, example_searchdomain, {}, datapoints))
        jsonEntities.append(jsonEntity)
    jsonstring = json.dumps(jsonEntities)
    timer_start = time.time()
    result:EntityIndexResult = toolset.Client.EntityIndexAsync(jsonstring).Result
    timer_end = time.time()
    toolset.Logger.LogInformation(f"Update was successful: {result.Success} - and was done in {timer_end - timer_start} seconds.")
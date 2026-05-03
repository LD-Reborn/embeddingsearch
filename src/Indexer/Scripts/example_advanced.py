import os
from tools import *
import json
from dataclasses import asdict
import time
import clr
from Indexer.Models import *

example_content = "./Scripts/example_content_advanced"
probmethod = "HVEWAvg"
similarityMethod = "Cosine"
example_searchdomain = "example_advanced_" + probmethod
example_counter = 0
models = ["ollama:bge-m3", "ollama:qwen3-embedding:8b-fp16"]
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

        content = toolset.DocumentProcessor.GetTextContentAsync(qualified_filepath).Result
        fullText = content.FullText
        datapoints:list = [
            JSONDatapoint("fileName", qualified_filepath, probmethod_datapoint, similarityMethod, models),
            JSONDatapoint("fullText", fullText, probmethod_datapoint, similarityMethod, models)
        ]
        attributes = {"fileName": qualified_filepath, "fullText": fullText}
        print("DEBUG@example_advanced")
        print(content)
        if str(content) == "Indexer.Models.DocumentProcessingTextResultModel" or str(content) == "Indexer.Models.DocumentProcessingImageResultModel":
            collections = []
        elif str(content) == "Indexer.Models.DocumentProcessingWordDocumentResultModel":
            collections = [
                ("paragraph", content.AsDictionary["Paragraphs"]),
                ("header", content.AsDictionary["HeaderParts"]),
                ("footer", content.AsDictionary["FooterParts"]),
                ("textbox", content.AsDictionary["TextBoxes"]),
                ("table", content.AsDictionary["Tables"]),
                ("comment", content.AsDictionary["Comments"]),
                ("image", content.AsDictionary["Images"])
            ]
        elif str(content) == "Indexer.Models.DocumentProcessingPdfResultModel":
            #content = DocumentProcessingPdfResultModel(content)
            collections = [
                ("page", content.AsDictionary["Pages"]),
                ("image", content.AsDictionary["Images"])
            ]
        else:
            print(f"Unknown content {str(content)}")
            exit()
        #print(collections)
        print("@loop")
        for prefix, items in collections:
            print(f"for prefix: {prefix} - {items.Count} - {len(list(items))} - {items} - {items}")
            for i in range(items.Count):
                item = items[i]
                print(item)
                attributeName = f"{prefix}_{i}"
                datapoints.append(JSONDatapoint(attributeName, item, probmethod_datapoint, similarityMethod, models))
                attributes[attributeName] = item
        jsonEntity:dict = asdict(JSONEntity(qualified_filepath, probmethod_entity, example_searchdomain, attributes, datapoints))
        jsonEntities.append(jsonEntity)
    jsonstring = json.dumps(jsonEntities)
    #print(jsonstring)
    timer_start = time.time()
    # Index all entities in one go. If you need to split it into chunks, use the session attributes! See example_chunked.py
    result:EntityIndexResult = toolset.Client.EntityIndexAsync(jsonstring).Result
    timer_end = time.time()
    toolset.Logger.LogInformation(f"Update was successful: {result.Success} - and was done in {timer_end - timer_start} seconds.")
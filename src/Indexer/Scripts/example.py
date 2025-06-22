import os
from tools import *
import json
from dataclasses import asdict
import time

example_content = "./Scripts/example_content"
probmethod = "DictionaryWeightedAverage"
example_searchdomain = "example_" + probmethod
example_counter = 0
models = ["bge-m3", "mxbai-embed-large"]
probmethod_datapoint = probmethod
probmethod_entity = probmethod
# Example for a dictionary based weighted average:
#   probmethod_datapoint = "DictionaryWeightedAverage:{\"bge-m3\": 4, \"mxbai-embed-large\": 1}"
#   probmethod_entity = "DictionaryWeightedAverage:{\"title\": 2, \"filename\": 0.1, \"text\": 0.25}"

def init(toolset: Toolset):
    global example_counter
    print("Py-DEBUG@init")
    print("This is the init function from the python example script")
    print(f"example_counter: {example_counter}")
    searchdomainlist:SearchdomainListResults = toolset.client.SearchdomainListAsync().Result
    if example_searchdomain not in searchdomainlist.Searchdomains:
        toolset.client.SearchdomainCreateAsync(example_searchdomain).Result
        searchdomainlist = toolset.client.SearchdomainListAsync().Result
    print("Currently these searchdomains exist:")
    for searchdomain in searchdomainlist.Searchdomains:
        print(f" - {searchdomain}")
    index_files(toolset)

def update(toolset: Toolset):
    global example_counter
    print("Py-DEBUG@update")
    print("This is the update function from the python example script")
    callbackInfos:ICallbackInfos = toolset.callbackInfos
    if (str(callbackInfos) == "Indexer.Models.IntervalCallbackInfos"):
        print("It was called via an interval callback")
    else:
        print("It was called, but the origin of the call could not be determined")
    example_counter += 1
    print(f"example_counter: {example_counter}")
    index_files(toolset)

def index_files(toolset: Toolset):
    jsonEntities:list = []
    for filename in os.listdir(example_content):
        qualified_filepath = example_content + "/" + filename
        with open(qualified_filepath, "r", encoding='utf-8') as file:
            title = file.readline()
            text = file.read()
        datapoints:list = [
            JSONDatapoint("filename", qualified_filepath, probmethod_datapoint, models),
            JSONDatapoint("title", title, probmethod_datapoint, models),
            JSONDatapoint("text", text, probmethod_datapoint, models)
        ]
        jsonEntity:dict = asdict(JSONEntity(qualified_filepath, probmethod_entity, example_searchdomain, {}, datapoints))
        jsonEntities.append(jsonEntity)
    jsonstring = json.dumps(jsonEntities)
    timer_start = time.time()
    result:EntityIndexResult = toolset.client.EntityIndexAsync(jsonstring).Result
    timer_end = time.time()
    print(f"Update was successful: {result.Success} - and was done in {timer_end - timer_start} seconds.")
import ollama
import json
import db
import models
import util
import os
import numpy as np
import time
import pickle

def fact(x):
    return 1/(1-x)

def wavg(arr):
    if 1 in arr:
        return 1
    f = [fact(x) for x in arr]
    fm = [x * fact(x) for x in arr]
    return np.sum(fm) / np.sum(f)


probmethods = models.Probmethods({"weighted_average": wavg})

connection = db.connection("localhost", "embeddingsearch", "ohmyllama!420wip", "embeddingsearch", probmethods)

# TODO does the entity need embedding handler? Remove it if not necessary


MODELS = ["mxbai-embed-large", "bge-m3", "paraphrase-multilingual", "nomic-embed-text"]
#MODELS = ["nomic-embed-text", "bge-m3"]


client = util.PicklableOllamaClient("http://192.168.0.101:11434")
embedding_handler = util.EmbeddingHandler(MODELS, client)
#searchdomain_settings = models.Searchdomain_settings()
#searchdomain = connection.searchdomain_create("testtarget", searchdomain_settings)
searchdomain = connection.searchdomain_get("testtarget", embedding_handler)

def index_file(filepath, entities:list[models.Entity] = None) -> bool:
    lastmodified = os.path.getmtime(filepath)
    if entities == None:
        previous_entity = connection.entity_get_by_name(filepath, searchdomain)
    else:
        previous_entity = [entity for entity in entities if entity.name == filepath]
    if previous_entity:
        previous_entity = previous_entity[0]
        lastmodified_prev = previous_entity.get_attribute("lastmodified")
        if lastmodified == float(lastmodified_prev.value):
            return False
    try:
        with open(filepath, "r") as file:
            text = file.read()
    except:
        return False
    entity = connection.entity_insert(filepath, "weighted_average", {"path": filepath, "type": "file", "contents": "text", "lastmodified": lastmodified}, [], searchdomain, embedding_handler)
    datapoint_filepath = connection.entity_insert_datapoint("filepath", "weighted_average", filepath, entity, embedding_handler)
    datapoint_content = connection.entity_insert_datapoint("content", "weighted_average", text, entity, embedding_handler)
    return True

def index_folder(path):
    time_start = time.time()
    entities = connection.entity_get_all(searchdomain)
    filecount_total = 0
    filecount_updated = 0
    for root, _, files in os.walk(path):
        for file in files:
            # This can easily be multiprocessed.
            # Push each filepath into a list of filepaths and then create a pool with it.
            filepath = os.path.join(root, file)
            retval = index_file(filepath, entities)
            if retval:
                filecount_updated += 1
            filecount_total += 1
    time_diff = time.time() - time_start
    print(f"TIME@index_folder: {time_diff * 1000} ms - {filecount_total / time_diff} files per second - {filecount_updated} files updated")

def search(text):
    time_start = time.time()
    results = connection.searchdomain_search(searchdomain, text, multithreaded=True)
    time_diff = time.time() - time_start
    print("Results")
    for i in range(10):
        result = results[i]
        result_certainty = result[0]
        result_entity = result[1]
        #print(f"Result {i}: {result_entity.name} {result_entity.attributes[0].value}")
        print(f"Result {i}: {result_entity}")
    #print(results)
    print(f"TIME@search: {round(time_diff * 1000, 2)} ms at an entity count of {len(results)} resulting in {round(len(results) / time_diff, 2)} results per second")
    print(f"Total entity cache size: {round(len(pickle.dumps(searchdomain.entity_cache)) / 1024 / 1024, 2)} MB")
    print(f"Total search cache size: {round(len(pickle.dumps(searchdomain.search_cache)) / 1024 / 1024, 2)} MB with {len(searchdomain.search_cache)} entries = {round(len(pickle.dumps(searchdomain.search_cache)) / len(searchdomain.search_cache) / 1024, 2)} KB/entry")

while True:
    verb = input("Please enter an action: index_folder to index a folder | index_file to index a single file | query or search to search for a text: ")

    match verb:
        case "index_folder":
            index_folder(input("Please input the folder to index: "))
        case "index_file":
            index_file(input("Please input the file to index: "))
        case "query" | "search":
            search(input("Please input what you want to search for: "))



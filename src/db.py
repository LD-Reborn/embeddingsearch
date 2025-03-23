import mysql.connector # use pip install and not apt!
import util
import models
import json
import time
import pickle
from multiprocessing import Pool
from multiprocessing import Manager
from functools import partial
import multiprocessing as mp
import numpy as np
import datetime

def search_embeddings(datapoint:models.Datapoint, text:str, embeddings):
    similarity_embeddings = []
    for datapoint_embedding in datapoint.embeddings:
        try:
            embedding = embeddings[datapoint_embedding.model]
        except:
            embedding = datapoint.embeddingHandler.create_specific(datapoint_embedding.model, text)
            embeddings[datapoint_embedding.model] = embedding
        datapoint_embedding_unpacked = datapoint.embeddingHandler.embeddings_unpack(datapoint_embedding.embedding)
        similarity_embeddings.append(datapoint.embeddingHandler.similarity(embedding, datapoint_embedding_unpacked))
    return similarity_embeddings

def search_entity(entity:models.Entity, probmethods, text, embeddings):
    similarity_datapoints = []
    for datapoint in entity.datapoints:
        similarity_embeddings = search_embeddings(datapoint, text, embeddings)
        similarity_datapoint = probmethods.methods[datapoint.probmethod](similarity_embeddings)
        similarity_datapoints.append(similarity_datapoint)
    return [probmethods.methods[entity.probmethod](similarity_datapoints), entity.name]#entity]

def search_entities(entities:list[models.Entity], probmethods, text, embeddings, outqueue:mp.Queue):
    for entity in entities:
        outqueue.put(search_entity(entity, probmethods, text, embeddings))
    return True

class connection:
    connection:mysql.connector
    probmethods:models.Probmethods

    def __init__(self, host, user, password, database, probmethods:models.Probmethods):
        self.connection = mysql.connector.connect(
            host=host,
            user=user,
            password=password,
            database=database
        )
        self.probmethods = probmethods

    def __execute_get_id(self, query, params:tuple):
        cursor = self.connection.cursor()
        cursor.execute(query, params)
        id = cursor.lastrowid
        self.connection.commit()
        return id

    def __execute_get_rowsaffected(self, query, params):
        cursor = self.connection.cursor()
        cursor.execute(query, params)
        cursor.commit()
        return cursor.rowcount

    def __execute_get_all(self, query, params):
        cursor = self.connection.cursor()
        cursor.execute(query, params)
        return cursor.fetchall()

    def __entity_get_conditional(self, condition_entity:str, condition_params:tuple, searchdomain:models.Searchdomain, name:str=None) -> list[models.Entity]:
        if not searchdomain.entity_cache_invalid:
            if name != None:
                for entity in searchdomain.entity_cache:
                    if entity.name == name:
                        return [entity]
            else:
                return searchdomain.entity_cache

        embedding = self.__execute_get_all(f"SELECT id, id_datapoint, model, embedding FROM embedding", ())
        datapoint = self.__execute_get_all(f"SELECT id, id_entity, name, probmethod_embedding FROM datapoint", ())
        attribute = self.__execute_get_all(f"SELECT id, id_entity, attribute, value FROM attribute", ())
        entity = self.__execute_get_all(f"SELECT id, name, probmethod FROM entity WHERE {condition_entity}", (condition_params)) # TODO BUG parameterize the condition!

        embedding_unassigned = {}
        for embedding_ in embedding: # (id, id_datapoint, model, embedding)
            embedding_id = embedding_[0]
            embedding_id_datapoint = embedding_[1]
            embedding_model = embedding_[2]
            embedding_embedding = embedding_[3]
            to_be_inserted = models.Embedding(embedding_id, embedding_model, embedding_embedding, embedding_id_datapoint) # TODO this "to_be_inserted" logic is pretty duplicate. Add method to handle this instead
            try:
                embedding_unassigned[embedding_id_datapoint].append(to_be_inserted)
            except:
                embedding_unassigned[embedding_id_datapoint] = [to_be_inserted]
        datapoint_unassigned = {}
        for datapoint_ in datapoint: # (id, id_entity, name, probmethod_embedding)
            datapoint_id = datapoint_[0]
            datapoint_id_entity = datapoint_[1]
            datapoint_name = datapoint_[2]
            datapoint_probmethod_embedding = datapoint_[3]
            to_be_inserted = models.Datapoint(datapoint_id, datapoint_name, datapoint_probmethod_embedding, None, searchdomain.default_embeddinghandler)
            try:
                datapoint_unassigned[datapoint_id_entity].append(to_be_inserted) # BUG EmbeddingHandler not set when importing from DB. What do?
            except:
                datapoint_unassigned[datapoint_id_entity] = [to_be_inserted] # BUG EmbeddingHandler not set when importing from DB. What do?
            try:
                embedding_assigned = embedding_unassigned.pop(datapoint_id)
                for embedding_ in embedding_assigned:
                    to_be_inserted.embeddings.append(embedding_)
            except:
                pass
        attribute_unassigned = {}
        for attribute_ in attribute: # (id, id_entity, attribute, value)
            attribute_id = attribute_[0]
            attribute_id_identity = attribute_[1]
            attribute_attribute = attribute_[2]
            attribute_value = attribute_[3]
            to_be_inserted = models.Attribute(attribute_id, attribute_attribute, attribute_value)
            try:
                attribute_unassigned[attribute_id_identity].append(to_be_inserted)
            except:
                attribute_unassigned[attribute_id_identity] = [to_be_inserted]
        entities = []
        for entity_ in entity: # (id, name, probmethod)
            entity_id = entity_[0]
            entity_name = entity_[1]
            entity_probmethod = entity_[2]
            try:
                properties = attribute_unassigned.pop(entity_id)
            except:
                properties = []
            try:
                datapoints = datapoint_unassigned.pop(entity_id)
            except:
                datapoints = []
            entities.append(models.Entity(entity_id, entity_name, properties, datapoints, entity_probmethod, searchdomain))
        embedding_unassigned = None # | Make sure no memory is hogged
        datapoint_unassigned = None # |
        attribute_unassigned = None # | 
        searchdomain.entity_cache = entities
        searchdomain.entity_cache_invalid = False
        return entities

    def __datapoint_generate_embeddings(self, datapoint:models.Datapoint):
        for model in datapoint.embeddingHandler.models:
            embedding = datapoint.embeddingHandler.embeddings_pack(datapoint.embeddingHandler.create_specific(model, datapoint.text))
            embedding_id = self.__execute_get_id("INSERT INTO embedding (id_datapoint, model, embedding) VALUES (%s, %s, %s)", (datapoint.id, model, embedding))
            datapoint.embeddings.append(models.Embedding(embedding_id, model, embedding, datapoint.id))
    
    def __datapoint_clear_embeddings(self, datapoint:models.Datapoint) -> bool:
        return self.__execute_get_rowsaffected("DELETE FROM embedding WHERE id_datapoint=%s", (datapoint.id,)) > 0

    def datapoint_update_embeddings(self, datapoint:models.Datapoint, text:str, clear_embeddings:bool):
        # TODO test this
        # TODO cache invalidation/revalidation - cache_revalidation_datapoint_update
        if clear_embeddings:
            self.__datapoint_clear_embeddings(datapoint)
        datapoint.text = text
        self.__datapoint_generate_embeddings(datapoint)
    
    def entity_insert(self, name, probmethod, attributes:dict, datapoints:list[models.Datapoint], searchdomain:models.Searchdomain, embeddingHandler:util.EmbeddingHandler) -> models.Entity:
        # TODO Test this
        # TODO cache invalidation/revalidation
        searchdomain.entity_cache_invalid = True
        if len(self.__execute_get_all("SELECT * FROM entity WHERE name=%s AND id_searchdomain=%s", (name, searchdomain.id))):
            entity = self.entity_get_by_name(name, searchdomain)[0]
            self.entity_delete(entity)
        attributes_ = []
        id_entity = self.__execute_get_id("INSERT INTO entity (name, probmethod, id_searchdomain) VALUES (%s, %s, %s)", (name, probmethod, searchdomain.id))
        for attribute, value in attributes.items():
            attribute_id = self.__execute_get_id("INSERT INTO attribute (id_entity, attribute, value) VALUES (%s, %s, %s)", (id_entity, attribute, value))
            attributes_.append(models.Attribute(attribute_id, attribute, value))
        
        for datapoint in datapoints:
            datapoint_name = datapoint.name
            datapoint_probmethod = datapoint.probmethod
            datapoint.parent_entity = id_entity # for good measure
            datapoint_id = self.__execute_get_id("INSERT INTO datapoint (name, probmethod_embedding, id_entity) VALUES (%s, %s, %s)", (datapoint_name, datapoint_probmethod, id_entity))
            datapoint.id = datapoint_id
            self.datapoint_update_embeddings(datapoint, datapoint.text, False)

        return models.Entity(id_entity, name, attributes_, datapoints, probmethod, searchdomain)

    def entity_insert_datapoint(self, name:str, probmethod:str, text:str, entity:models.Entity, embeddinghandler:util.EmbeddingHandler) -> models.Datapoint:
        # TODO test this
        # TODO cache invalidation/revalidation - cache_revalidation_datapoint_create
        entity.searchdomain.entity_cache_invalid = True
        datapoint_id = self.__execute_get_id("INSERT INTO datapoint (name, probmethod_embedding, id_entity) VALUES (%s, %s, %s)", (name, probmethod, entity.id))
        datapoint = models.Datapoint(datapoint_id, name, probmethod, text, embeddinghandler)
        self.datapoint_update_embeddings(datapoint, text, False)

    def entity_delete(self, entity:models.Entity, searchdomain:models.Searchdomain):
        # TODO test this
        searchdomain.entity_cache_invalid = True
        for datapoint in entity.datapoints:
            self.__execute_get_id("DELETE FROM embedding WHERE id_datapoint=%s", (datapoint.id,))
            self.__execute_get_id("DELETE FROM datapoint WHERE id=%s", (datapoint.id,))
        self.__execute_get_id("DELETE FROM attribute WHERE id_entity=%s", (entity.id,))
        self.__execute_get_id("DELETE FROM entity WHERE id=%s", (entity.id,))
        # TODO query cache invalidation/revalidation

    def entity_update_attribute(self, entity:models.Entity, attribute:str, value:str, create_if_not_exists=True) -> bool:
        # TODO test this
        rowsaffected = self.__execute_get_rowsaffected("UPDATE attribute SET value=%s WHERE name=%s", (value, attribute))
        if create_if_not_exists and rowsaffected <= 0:
            self.__execute_get_id("INSERT INTO attribute (id_identity, attribute, value) VALUES (%s, %s, %s)", (entity.id, attribute, value))
            return True
        elif not create_if_not_exists and rowsaffected <= 0:
            return False
        return True

    def entity_update_datapoint_name(self, datapoint:models.Datapoint, new_datapoint_name:str) -> bool:
        # TODO test this
        rowsaffected = self.__execute_get_rowsaffected("UPDATE datapoint SET name=%s WHERE id=%s", (new_datapoint_name, datapoint.id))
        return rowsaffected > 0

    def entity_update_datapoint_probmethod(self, datapoint:models.Datapoint, new_probmethod:str, searchdomain:models.Searchdomain):
        # TODO test this
        searchdomain.entity_cache_invalid = True
        # TODO cache invalidation/revalidation - cache_revalidation_datapoint_update
        rowsaffected = self.__execute_get_rowsaffected("UPDATE datapoint SET probmethod=%s WHERE id=%s", (new_probmethod, datapoint.id))
        return rowsaffected > 0

    def entity_delete_attribute(self, entity:models.Entity, attribute_name:str) -> bool:
        # TODO test this
        return self.__execute_get_rowsaffected("DELETE FROM attribute WHERE id_entity=%s", (entity.id,)) > 0

    def entity_delete_datapoint(self, datapoint:models.Datapoint) -> bool:
        # TODO test this
        # TODO cache invalidation/revalidation - cache_revalidation_datapoint_remove
        self.__execute_get_rowsaffected("DELETE FROM embedding WHERE id_datapoint=%s", (datapoint.id,))
        return self.__execute_get_rowsaffected("DELETE FROM datapoint WHERE id=%s", (datapoint.id,)) > 0

    def entity_get_all(self, searchdomain:models.Searchdomain) -> list[models.Entity]:
        return self.__entity_get_conditional("1 = 1", (), searchdomain)

    def entity_get_by_name(self, name:str, searchdomain:models.Searchdomain) -> list[models.Entity]:
        return self.__entity_get_conditional(f"name = %s", (name,), searchdomain) # TODO parameterize this

    def searchdomain_create(self, name:str, settings:models.Searchdomain_settings, default_embeddinghandler:util.EmbeddingHandler) -> models.Searchdomain:
        id_searchdomain = self.__execute_get_id("INSERT INTO searchdomain (name, settings) VALUES (%s, %s)", (name, settings.as_json()))
        return models.Searchdomain(id_searchdomain, name, settings, default_embeddinghandler)

    def searchdomain_get(self, name:str, default_embeddinghandler:util.EmbeddingHandler, create_if_not_exists:bool=True) -> models.Searchdomain:
        searchdomain = self.__execute_get_all("SELECT id, name, settings FROM searchdomain WHERE name=%s", (name,))
        if len(searchdomain) == 0 and create_if_not_exists:
            self.searchdomain_create(name, models.Searchdomain_settings(), default_embeddinghandler)
            return self.searchdomain_get(name, default_embeddinghandler)
        return models.Searchdomain(searchdomain[0][0], searchdomain[0][1], models.Searchdomain_settings().from_json(searchdomain[0][2]), default_embeddinghandler)

    def searchdomain_get_all(self, default_embeddinghandler:util.EmbeddingHandler) -> models.Searchdomain:
        searchdomain = self.__execute_get_all("SELECT id, name, settings FROM searchdomain", ())
        searchdomains = []
        for searchdomain_ in searchdomain:
            searchdomains.append(models.Searchdomain(searchdomain[0], searchdomain[1], models.Searchdomain_settings().from_json(searchdomain[2]), default_embeddinghandler))
        return searchdomains

    def searchdomain_delete(self, searchdomain:models.Searchdomain) -> bool:
        # TODO test this
        searchdomain = self.__execute_get_rowsaffected("DELETE FROM searchdomain WHERE id=%s", (searchdomain.id,))
        return searchdomain > 0

    def searchdomain_update_name(self, searchdomain:models.Searchdomain, new_name:str):
        # TODO test this
        searchdomain = self.__execute_get_rowsaffected("UPDATE searchdomain SET name=%s WHERE id=%s", (new_name, searchdomain.id))
        return searchdomain > 0

    def searchdomain_update_setting(self, searchdomain:models.Searchdomain, setting:str, value:any):
        # TODO test this
        searchdomain = self.__execute_get_rowsaffected("UPDATE searchdomain SET settings=JSON_SET(settings, '$.%s', %s) WHERE id=%s", (setting, value, searchdomain.id))
        return searchdomain > 0

    def searchdomain_search(self, searchdomain:models.Searchdomain, text:str, limit_results:int=None, multithreaded=True) -> list[float, str]:
        if searchdomain.search_cache.__contains__(text):
            searchresult:models.Searchresult = searchdomain.search_cache[text]
            searchresult.last_access_date = datetime.datetime.now()
            return searchresult.results
        time1 = time.time()
        entities = self.entity_get_all(searchdomain)
        if multithreaded:
            with Manager() as manager:
                print("a")
                print(time.time()-time1)
                embeddings = manager.dict()#Value('d', {}) # Multiprocessing-safe
                print("b")
                print(time.time()-time1)
                similarity_entities = []
                print("c")
                print(time.time()-time1)
                cpu_count = mp.cpu_count()
                print("d")
                print(time.time()-time1)
                outqueue = mp.Queue(len(entities))
                print("e")
                print(time.time()-time1)
                entities_ = np.array_split(entities, cpu_count)
                print("f")
                print(time.time()-time1)
                processes:list[mp.Process] = [None] * cpu_count
                print("g")
                print(time.time()-time1)
                for i in range(cpu_count):
                    processes[i] = mp.Process(target=search_entities, args=(entities_[i], self.probmethods, text, embeddings, outqueue))
                for process in processes:
                    process.start()
                print("h")
                print(time.time()-time1)
                while len(similarity_entities) < len(entities):
                    if outqueue.qsize():
                        #print(f"got something from queue {outqueue.qsize()} - {len(similarity_entities)} - {len(entities_)}")
                        similarity_entities.append(outqueue.get())
                    else:
                        #print(processes[0].is_alive())
                        #print(outqueue.qsize())
                        pass#time.sleep(0.01)
                print("i")
                print(time.time()-time1)
                for process in processes:
                    if process.is_alive():
                        process.join()
                        process.close()
                print("j")
                print(time.time()-time1)
                print(outqueue.qsize())
                outqueue.close()
                #print(similarity_entities)
                #with Pool() as pool:
                #    method = search_entity
                #    partial_search_entity = partial(method, probmethods=self.probmethods, text=text, embeddings=embeddings)
                #    time1 = time.time()
                #    similarity_entities = pool.map(partial_search_entity, entities)
                #    print(time.time() - time1)
                #    print(f"DEBUG@searchdomain_search - {len(pickle.dumps(entities))} - {len(pickle.dumps(self.probmethods))} - {len(pickle.dumps(text))} - {len(pickle.dumps(embeddings))} - {len(pickle.dumps(similarity_entities))}")
                #    # Entities is 24.6 MB, which is a lot, and it appears to take long to transfer.
                #    # Doing multithreading this way currently reduces the execution time from ~1200ms to ~800ms, so not much.
                #    # 
                #    #similarity_entities = pool.map(self.__search_entity, entities)
        else:
            embeddings = {}
            similarity_entities = []
            for entity in entities:
                similarity_datapoints = []
                for datapoint in entity.datapoints:
                    similarity_embeddings = search_embeddings(datapoint, text, embeddings)
                    similarity_datapoint = self.probmethods.methods[datapoint.probmethod](similarity_embeddings)
                    similarity_datapoints.append(similarity_datapoint)
                similarity_entities.append([self.probmethods.methods[entity.probmethod](similarity_datapoints), entity.name]) #entity])
        print(time.time()-time1)
        results = sorted(similarity_entities, key=lambda x: x[0], reverse=True)
        print(time.time()-time1)

        if len(searchdomain.search_cache) < searchdomain.settings.cache_maxentries:
            searchdomain.search_cache[text] = models.Searchresult(text, datetime.datetime.now(), results)

        if limit_results:
            return results[:limit_results]
        return results
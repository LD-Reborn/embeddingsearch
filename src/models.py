import util
import json
import datetime

class Embedding:
    id:int
    model:str
    embedding:bytearray # TODO check if this can be bytes
    parent_datapoint:int

    def __init__(self, id, model, embedding, parent_datapoint:int):   
        self.id = id
        self.model = model
        self.embedding = embedding
        self.parent_datapoint = parent_datapoint

class Datapoint:
    id:int
    name:str
    probmethod:str
    text:str
    embeddings:list[Embedding]
    embeddingHandler:util.EmbeddingHandler
    parent_entity:int

    def __init__(self, id:int, name:str, probmethod:str, text:str, embeddingHandler:util.EmbeddingHandler):
        self.id = id
        self.name = name
        self.probmethod = probmethod
        self.text = text
        self.embeddings = []
        self.embeddingHandler = embeddingHandler

class Attribute:
    id:int
    attribute:str
    value:str

    def __init__(self, id:int, attribute:str, value:str):
        self.id = id
        self.attribute = attribute
        self.value = value


class Searchdomain_settings:
    cache_maxentries:int
    cache_revalidation_entity_add:bool # When an entity is added to the search domain, what do? True: 
    cache_revalidation_entity_remove:bool
    cache_revalidation_embedding_update:bool
    cache_revalidation_datapoint_create:bool
    cache_revalidation_datapoint_update:bool
    cache_revalidation_datapoint_remove:bool

    def __init__(self, cache_maxentries = 10000, cache_revalidation_entity_add = True, cache_revalidation_entity_remove = True, cache_revalidation_embedding_update = True, cache_revalidation_datapoint_create = True, cache_revalidation_datapoint_update = True, cache_revalidation_datapoint_remove = True):
        self.cache_maxentries = cache_maxentries
        self.cache_revalidation_entity_add = cache_revalidation_entity_add
        self.cache_revalidation_entity_remove = cache_revalidation_entity_remove
        self.cache_revalidation_embedding_update = cache_revalidation_embedding_update
        self.cache_revalidation_datapoint_create = cache_revalidation_datapoint_create
        self.cache_revalidation_datapoint_update = cache_revalidation_datapoint_update
        self.cache_revalidation_datapoint_remove = cache_revalidation_datapoint_remove
    
    def as_json(self):
        return json.dumps({"cache_maxentries": self.cache_maxentries,
                   "cache_revalidation_entity_add": self.cache_revalidation_entity_add,
                   "cache_revalidation_entity_remove": self.cache_revalidation_entity_remove,
                   "cache_revalidation_embedding_update": self.cache_revalidation_embedding_update,
                   "cache_revalidation_datapoint_create": self.cache_revalidation_datapoint_create,
                   "cache_revalidation_datapoint_update": self.cache_revalidation_datapoint_update,
                   "cache_revalidation_datapoint_remove": self.cache_revalidation_datapoint_remove})
    
    def from_json(self, jsonstr):
        data = json.loads(jsonstr)
        try:
            self.cache_maxentries = data["cache_maxentries"]
        except:
            pass
        try:
            self.cache_revalidation_entity_add = data["cache_revalidation_entity_add"]
        except:
            pass
        try:
            self.cache_revalidation_entity_remove = data["cache_revalidation_entity_remove"]
        except:
            pass
        try:
            self.cache_revalidation_embedding_update = data["cache_revalidation_embedding_update"]
        except:
            pass
        try:
            self.cache_revalidation_datapoint_create = data["cache_revalidation_datapoint_create"]
        except:
            pass
        try:
            self.cache_revalidation_datapoint_update = data["cache_revalidation_datapoint_update"]
        except:
            pass
        try:
            self.cache_revalidation_datapoint_remove = data["cache_revalidation_datapoint_remove"]
        except:
            pass
        return self

class Searchresult:
    text:str
    last_access_date:datetime
    results:list[float, str]
    
    def __init__(self, text, last_access_date, results):
        self.text = text
        self.last_access_date = last_access_date
        self.results = results

class Searchdomain:
    id:int
    name:str
    settings:Searchdomain_settings
    default_embeddinghandler:util.EmbeddingHandler
    entity_cache:dict
    entity_cache_invalid:bool
    search_cache:dict

    def __init__(self, id:int, name:str, settings:Searchdomain_settings, default_embeddinghandler:util.EmbeddingHandler):
        self.id = id
        self.name = name
        self.settings = settings
        self.default_embeddinghandler = default_embeddinghandler
        self.entity_cache = {}
        self.entity_cache_invalid = True
        self.search_cache = {}

class Entity:
    id:int
    name:str
    attributes:list[Attribute]
    datapoints:list[Datapoint]
    probmethod:str
    searchdomain:Searchdomain

    def __init__(self, id:int, name:str, attributes:list[Attribute], datapoints:list[Datapoint], probmethod:str, searchdomain:Searchdomain):
        self.id = id
        self.name = name
        self.attributes = attributes
        self.datapoints = datapoints
        self.probmethod = probmethod
        self.searchdomain = searchdomain

    def get_attribute(self, attribute:str) -> Attribute:
        attributes = [attribute_ for attribute_ in self.attributes if attribute_.attribute == attribute]
        if len(attributes):
            return attributes[0]
        return None # "-> Attribute:" That was a lie


class Probmethods: # Kinda overkill. The class itself is itself technically just a dict anyway, so why bother?
    # TODO check if this can be removed after initial tests
    methods:dict

    def __init__(self, methods:dict):
        self.methods = methods


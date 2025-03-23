import ollama
import pickle
import numpy as np
from numpy.linalg import norm
import struct
import threading

class PicklableOllamaClient(ollama.Client): # This feels really wrong
    def __getstate__(self):
        return {}

    def __setstate__(self, state):
        self.__init__()

class EmbeddingHandler:
    models:list[str]
    client:PicklableOllamaClient

    def __init__(self, models:list[str], client:ollama.Client):
        self.models = models
        self.client = client

    def embeddings_pack(self, embedding:list[float]) -> bytes:
        return struct.pack('f' * int(len(embedding)), *embedding)

    def embeddings_unpack(self, embedding:bytes) -> tuple:
        return struct.unpack('f' * int(len(embedding) / 4), embedding)

    def create_specific(self, model, text):
        response = self.client.embeddings(model=model, prompt=text)
        return response["embedding"] if "embedding" in response else None
    
    def create(self, text):
        response = []
        for model in self.models:
            embedding = self.client.embeddings(model=model, prompt=text)
            response.append(embedding["embedding"] if "embedding" in embedding else None)
        return response
    
    def similarity(self, a, b): # Takes list[float] but also tuple[float]. That's why sometimes things don't need to be strictly typed :)
        try:
            return np.dot(a,b)/(norm(a)*norm(b))
        except:
            return 0

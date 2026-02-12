using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdaptiveExpressions;
using Server.Exceptions;
using Shared;
using Shared.Models;

namespace Server.Helper;

public class SearchdomainHelper(ILogger<SearchdomainHelper> logger, DatabaseHelper databaseHelper)
{
    private readonly ILogger<SearchdomainHelper> _logger = logger;
    private readonly DatabaseHelper _databaseHelper = databaseHelper;

    public static byte[] BytesFromFloatArray(float[] floats)
    {
        var byteArray = new byte[floats.Length * sizeof(float)];
        var floatArray = floats.ToArray();
        Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);
        return byteArray;
    }

    public static float[] FloatArrayFromBytes(byte[] bytes)
    {
        var floatArray = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floatArray, 0, bytes.Length);
        return floatArray;
    }

    public static bool CacheHasEntity(ConcurrentBag<Entity> entityCache, string name)
    {
        return CacheGetEntity(entityCache, name) is not null;
    }

    public static Entity? CacheGetEntity(ConcurrentBag<Entity> entityCache, string name)
    {
        foreach (Entity entity in entityCache)
        {
            if (entity.name == name)
            {
                return entity;
            }
        }
        return null;
    }

    public List<Entity>? EntitiesFromJSON(SearchdomainManager searchdomainManager, ILogger logger, string json)
    {
        EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache = searchdomainManager.embeddingCache;
        AIProvider aIProvider = searchdomainManager.aIProvider;
        SQLHelper helper = searchdomainManager.helper;

        List<JSONEntity>? jsonEntities = JsonSerializer.Deserialize<List<JSONEntity>>(json);
        if (jsonEntities is null)
        {
            return null;
        }

        // Prefetch embeddings
        Dictionary<string, List<string>> toBeCached = [];
        Dictionary<string, List<string>> toBeCachedParallel = [];
        foreach (JSONEntity jSONEntity in jsonEntities)
        {
            Dictionary<string, List<string>> targetDictionary = toBeCached;
            if (searchdomainManager.GetSearchdomain(jSONEntity.Searchdomain).settings.ParallelEmbeddingsPrefetch)
            {
                targetDictionary = toBeCachedParallel;
            }
            foreach (JSONDatapoint datapoint in jSONEntity.Datapoints)
            {
                foreach (string model in datapoint.Model)
                {
                    if (!targetDictionary.ContainsKey(model))
                    {
                        targetDictionary[model] = [];
                    }
                    targetDictionary[model].Add(datapoint.Text);
                }
            }
        }
        
        foreach (var toBeCachedKV in toBeCached)
        {
            string model = toBeCachedKV.Key;
            List<string> uniqueStrings = [.. toBeCachedKV.Value.Distinct()];
            Datapoint.GetEmbeddings([.. uniqueStrings], [model], aIProvider, embeddingCache);
        }
        Parallel.ForEach(toBeCachedParallel, toBeCachedParallelKV =>
        {
            string model = toBeCachedParallelKV.Key;
            List<string> uniqueStrings = [.. toBeCachedParallelKV.Value.Distinct()];
            Datapoint.GetEmbeddings([.. uniqueStrings], [model], aIProvider, embeddingCache);
        });
        // Index/parse the entities
        ConcurrentQueue<Entity> retVal = [];
        ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = 16 }; // <-- This is needed! Otherwise if we try to index 100+ entities at once, it spawns 100 threads, exploding the SQL pool
        Parallel.ForEach(jsonEntities, parallelOptions, jSONEntity =>
        {
            var entity = EntityFromJSON(searchdomainManager, logger, jSONEntity);
            if (entity is not null)
            {
                retVal.Enqueue(entity);
            }
        });
        return [.. retVal];
    }

    public Entity? EntityFromJSON(SearchdomainManager searchdomainManager, ILogger logger, JSONEntity jsonEntity) //string json)
    {
        using SQLHelper helper = searchdomainManager.helper.DuplicateConnection();
        Searchdomain searchdomain = searchdomainManager.GetSearchdomain(jsonEntity.Searchdomain);
        ConcurrentBag<Entity> entityCache = searchdomain.entityCache;
        AIProvider aIProvider = searchdomain.aIProvider;
        EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache = searchdomain.embeddingCache;
        Entity? preexistingEntity;
        lock (entityCache)
        {
            preexistingEntity = entityCache.FirstOrDefault(entity => entity.name == jsonEntity.Name);
        }
        bool invalidateSearchCache = false;

        if (preexistingEntity is not null)
        {
            int? preexistingEntityID = _databaseHelper.GetEntityID(helper, jsonEntity.Name, jsonEntity.Searchdomain);
            if (preexistingEntityID is null)
            {
                _logger.LogCritical("Unable to index entity {jsonEntity.Name} because it already exists in the searchdomain but not in the database.", [jsonEntity.Name]);
                throw new Exception($"Unable to index entity {jsonEntity.Name} because it already exists in the searchdomain but not in the database.");
            }
            Dictionary<string, string> attributes = jsonEntity.Attributes;
            
            // Attribute - get changes
            List<(string attribute, string newValue, int entityId)> updatedAttributes = [];
            List<(string attribute, int entityId)> deletedAttributes = [];
            List<(string attributeKey, string attribute, int entityId)> addedAttributes = [];
            foreach (KeyValuePair<string, string> attributesKV in preexistingEntity.attributes.ToList())
            {
                string oldAttributeKey = attributesKV.Key;
                string oldAttribute = attributesKV.Value;
                bool newHasAttribute = jsonEntity.Attributes.TryGetValue(oldAttributeKey, out string? newAttribute);
                if (newHasAttribute && newAttribute is not null && newAttribute != oldAttribute)
                {
                    updatedAttributes.Add((attribute: oldAttributeKey, newValue: newAttribute, entityId: (int)preexistingEntityID));
                } else if (!newHasAttribute)
                {
                    deletedAttributes.Add((attribute: oldAttributeKey, entityId: (int)preexistingEntityID));
                }
            }
            foreach (var attributesKV in jsonEntity.Attributes)
            {
                string newAttributeKey = attributesKV.Key;
                string newAttribute = attributesKV.Value;
                bool preexistingHasAttribute = preexistingEntity.attributes.TryGetValue(newAttributeKey, out string? preexistingAttribute);
                if (!preexistingHasAttribute)
                {
                    // Attribute - New
                    addedAttributes.Add((attributeKey: newAttributeKey, attribute: newAttribute, entityId: (int)preexistingEntityID));
                }
            }

            
            // Attribute - apply changes
            if (updatedAttributes.Count != 0)
            {
                // Update
                DatabaseHelper.DatabaseUpdateAttributes(helper, updatedAttributes);
                lock (preexistingEntity.attributes)
                {
                    updatedAttributes.ForEach(attribute => preexistingEntity.attributes[attribute.attribute] = attribute.newValue);
                }
            }
            if (deletedAttributes.Count != 0)
            {
                // Delete
                DatabaseHelper.DatabaseDeleteAttributes(helper, deletedAttributes);
                lock (preexistingEntity.attributes)
                {
                    deletedAttributes.ForEach(attribute => preexistingEntity.attributes.Remove(attribute.attribute));
                }
            }
            if (addedAttributes.Count != 0)
            {
                // Insert
                DatabaseHelper.DatabaseInsertAttributes(helper, addedAttributes);
                lock (preexistingEntity.attributes)
                {
                    addedAttributes.ForEach(attribute => preexistingEntity.attributes.Add(attribute.attributeKey, attribute.attribute));
                }
            }

            // Datapoint - get changes
            List<Datapoint> deletedDatapointInstances = [];
            List<string> deletedDatapoints = [];
            List<(string datapointName, int entityId, JSONDatapoint jsonDatapoint, string hash)> updatedDatapointsText = [];
            List<(string datapointName, string probMethod, string similarityMethod, int entityId, JSONDatapoint jsonDatapoint)> updatedDatapointsNonText = [];
            List<Datapoint> createdDatapointInstances = [];
            List<(string name, ProbMethodEnum probmethod_embedding, SimilarityMethodEnum similarityMethod, string hash, Dictionary<string, float[]> embeddings, JSONDatapoint datapoint)> createdDatapoints = [];
            
            foreach (Datapoint datapoint_ in preexistingEntity.datapoints.ToList())
            {
                Datapoint datapoint = datapoint_; // To enable replacing the datapoint reference as foreach iterators cannot be overwritten
                bool newEntityHasDatapoint = jsonEntity.Datapoints.Any(x => x.Name == datapoint.name);
                if (!newEntityHasDatapoint)
                {
                    // Datapoint - Deleted
                    deletedDatapointInstances.Add(datapoint);
                    deletedDatapoints.Add(datapoint.name);
                    invalidateSearchCache = true;
                } else
                {
                    JSONDatapoint? newEntityDatapoint = jsonEntity.Datapoints.FirstOrDefault(x => x.Name == datapoint.name);
                    string? hash = newEntityDatapoint?.Text is not null ? GetHash(newEntityDatapoint) : null;
                    if (
                        newEntityDatapoint is not null
                        && newEntityDatapoint.Text is not null
                        && hash is not null
                        && hash != datapoint.hash)
                    {
                        // Datapoint - Updated (text)
                        updatedDatapointsText.Add(new()
                        {
                            datapointName = newEntityDatapoint.Name,
                            entityId = (int)preexistingEntityID,
                            jsonDatapoint = newEntityDatapoint,
                            hash = hash
                        });
                        invalidateSearchCache = true;
                    }
                    if (
                        newEntityDatapoint is not null
                        && (newEntityDatapoint.Probmethod_embedding != datapoint.probMethod.probMethodEnum
                            || newEntityDatapoint.SimilarityMethod != datapoint.similarityMethod.similarityMethodEnum))
                    {
                        // Datapoint - Updated (probmethod or similaritymethod)
                        updatedDatapointsNonText.Add(new()
                        {
                            datapointName = newEntityDatapoint.Name,
                            entityId = (int)preexistingEntityID,
                            probMethod = newEntityDatapoint.Probmethod_embedding.ToString(),
                            similarityMethod = newEntityDatapoint.SimilarityMethod.ToString(),
                            jsonDatapoint = newEntityDatapoint
                        });
                        invalidateSearchCache = true;
                    }
                }
            }
            foreach (JSONDatapoint jsonDatapoint in jsonEntity.Datapoints)
            {
                bool oldEntityHasDatapoint = preexistingEntity.datapoints.Any(x => x.name == jsonDatapoint.Name);
                if (!oldEntityHasDatapoint)
                {
                    // Datapoint - New
                    createdDatapoints.Add(new()
                    {
                        name = jsonDatapoint.Name,
                        probmethod_embedding = jsonDatapoint.Probmethod_embedding,
                        similarityMethod = jsonDatapoint.SimilarityMethod,
                        hash = GetHash(jsonDatapoint),
                        embeddings = Datapoint.GetEmbeddings(
                            jsonDatapoint.Text ?? throw new Exception("jsonDatapoint.Text must not be null when retrieving embeddings"),
                            [.. jsonDatapoint.Model],
                            aIProvider,
                            embeddingCache
                        ),
                        datapoint = jsonDatapoint
                    });
                    invalidateSearchCache = true;
                }
            }
            
            // Datapoint - apply changes
            // Deleted
            if (deletedDatapointInstances.Count != 0)
            {
                DatabaseHelper.DatabaseDeleteDatapoints(helper, deletedDatapoints, (int)preexistingEntityID);
                deletedDatapointInstances.ForEach(datapoint => preexistingEntity.datapoints.Remove(datapoint));
            }
            // Created
            if (createdDatapoints.Count != 0)
            {
                List<Datapoint> datapoint = DatabaseInsertDatapointsWithEmbeddings(helper, searchdomain, [.. createdDatapoints.Select(element => (element.datapoint, element.hash))], (int)preexistingEntityID);
                createdDatapoints.ForEach(datapoint => preexistingEntity.datapoints.Add(new(
                    datapoint.name,
                    datapoint.probmethod_embedding,
                    datapoint.similarityMethod,
                    datapoint.hash,
                    [.. datapoint.embeddings.Select(element => (element.Key, element.Value))])
                ));
            }
            // Datapoint - Updated (text)
            if (updatedDatapointsText.Count != 0)
            {
                DatabaseHelper.DatabaseDeleteDatapoints(helper, [.. updatedDatapointsText.Select(datapoint => datapoint.datapointName)], (int)preexistingEntityID);
                updatedDatapointsText.ForEach(datapoint => preexistingEntity.datapoints.RemoveAll(x => x.name == datapoint.datapointName));
                List<Datapoint> datapoints = DatabaseInsertDatapointsWithEmbeddings(helper, searchdomain, [.. updatedDatapointsText.Select(element => (datapoint: element.jsonDatapoint, hash: element.hash))], (int)preexistingEntityID);
                preexistingEntity.datapoints.AddRange(datapoints);
            }
            // Datapoint - Updated (probmethod or similaritymethod)
            if (updatedDatapointsNonText.Count != 0)
            {
                DatabaseHelper.DatabaseUpdateDatapoint(
                    helper,
                    [.. updatedDatapointsNonText.Select(element => (element.datapointName, element.probMethod, element.similarityMethod))],
                    (int)preexistingEntityID
                );
                updatedDatapointsNonText.ForEach(element =>
                {
                    Datapoint preexistingDatapoint = preexistingEntity.datapoints.First(x => x.name == element.datapointName);
                    preexistingDatapoint.probMethod = new(element.jsonDatapoint.Probmethod_embedding);
                    preexistingDatapoint.similarityMethod = new(element.jsonDatapoint.SimilarityMethod);
                });
            } 

            if (invalidateSearchCache)
            {
                searchdomain.ReconciliateOrInvalidateCacheForNewOrUpdatedEntity(preexistingEntity);
            }
            searchdomain.UpdateModelsInUse();
            return preexistingEntity;
        }
        else
        {
            int id_entity = DatabaseHelper.DatabaseInsertEntity(helper, jsonEntity.Name, jsonEntity.Probmethod, _databaseHelper.GetSearchdomainID(helper, jsonEntity.Searchdomain));
            List<(string attribute, string value, int id_entity)> toBeInsertedAttributes = [];
            foreach (KeyValuePair<string, string> attribute in jsonEntity.Attributes)
            {
                toBeInsertedAttributes.Add(new() {
                    attribute = attribute.Key,
                    value = attribute.Value,
                    id_entity = id_entity
                });
            }
            DatabaseHelper.DatabaseInsertAttributes(helper, toBeInsertedAttributes);

            List<(JSONDatapoint datapoint, string hash)> toBeInsertedDatapoints = [];
            foreach (JSONDatapoint jsonDatapoint in jsonEntity.Datapoints)
            {
                string hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jsonDatapoint.Text)));
                toBeInsertedDatapoints.Add(new()
                {
                    datapoint = jsonDatapoint,
                    hash = hash
                });
            }
            List<Datapoint> datapoints = DatabaseInsertDatapointsWithEmbeddings(helper, searchdomain, toBeInsertedDatapoints, id_entity);
            
            var probMethod = Probmethods.GetMethod(jsonEntity.Probmethod) ?? throw new ProbMethodNotFoundException(jsonEntity.Probmethod);
            Entity entity = new(jsonEntity.Attributes, probMethod, jsonEntity.Probmethod.ToString(), datapoints, jsonEntity.Name)
            {
                id = id_entity
            };
            entityCache.Add(entity);
            searchdomain.ReconciliateOrInvalidateCacheForNewOrUpdatedEntity(entity);
            searchdomain.UpdateModelsInUse();
            return entity;
        }
    }

    public List<Datapoint> DatabaseInsertDatapointsWithEmbeddings(SQLHelper helper, Searchdomain searchdomain, List<(JSONDatapoint datapoint, string hash)> values, int id_entity)
    {
        List<Datapoint> result = [];
        List<(string name, ProbMethodEnum probmethod_embedding, SimilarityMethodEnum similarityMethod, string hash)> toBeInsertedDatapoints = [];
        List<(string name, string model, byte[] embedding)> toBeInsertedEmbeddings = [];
        foreach ((JSONDatapoint datapoint, string hash) value in values)
        {
            Datapoint datapoint = BuildDatapointFromJsonDatapoint(value.datapoint, id_entity, searchdomain, value.hash);
            toBeInsertedDatapoints.Add(new()
            {
                name = datapoint.name,
                probmethod_embedding = datapoint.probMethod.probMethodEnum,
                similarityMethod = datapoint.similarityMethod.similarityMethodEnum,
                hash = value.hash
            });
            foreach ((string, float[]) embedding in datapoint.embeddings)
            {
                toBeInsertedEmbeddings.Add(new()
                {
                    name = datapoint.name,
                    model = embedding.Item1,
                    embedding = BytesFromFloatArray(embedding.Item2)
                });
            }
            result.Add(datapoint);
        }
        
        int insertedDatapoints = DatabaseHelper.DatabaseInsertDatapoints(helper, toBeInsertedDatapoints, id_entity);
        int insertedEmbeddings = DatabaseHelper.DatabaseInsertEmbeddingBulk(helper, toBeInsertedEmbeddings, id_entity);
        return result;
    }

    public Datapoint DatabaseInsertDatapointWithEmbeddings(SQLHelper helper, Searchdomain searchdomain, JSONDatapoint jsonDatapoint, int id_entity, string? hash = null)
    {
        if (jsonDatapoint.Text is null)
        {
            throw new Exception("jsonDatapoint.Text must not be null at this point");
        }
        hash ??= GetHash(jsonDatapoint);
        Datapoint datapoint = BuildDatapointFromJsonDatapoint(jsonDatapoint, id_entity, searchdomain, hash);
        int id_datapoint = DatabaseHelper.DatabaseInsertDatapoint(helper, jsonDatapoint.Name, jsonDatapoint.Probmethod_embedding, jsonDatapoint.SimilarityMethod, hash, id_entity); // TODO make this a bulk add action to reduce number of queries
        List<(string model, byte[] embedding)> data = [];
        foreach ((string, float[]) embedding in datapoint.embeddings)
        {
            data.Add((embedding.Item1, BytesFromFloatArray(embedding.Item2)));
        }
        DatabaseHelper.DatabaseInsertEmbeddingBulk(helper, id_datapoint, data);
        return datapoint;
    }

    public string GetHash(JSONDatapoint jsonDatapoint)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jsonDatapoint.Text ?? throw new Exception("jsonDatapoint.Text must not be null to compute hash"))));
    }

    public Datapoint BuildDatapointFromJsonDatapoint(JSONDatapoint jsonDatapoint, int entityId, Searchdomain searchdomain, string? hash = null)
    {
        if (jsonDatapoint.Text is null)
        {
            throw new Exception("jsonDatapoint.Text must not be null at this point");
        }
        using SQLHelper helper = searchdomain.helper.DuplicateConnection();
        EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache = searchdomain.embeddingCache;
        hash ??= Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jsonDatapoint.Text)));
        DatabaseHelper.DatabaseInsertDatapoint(helper, jsonDatapoint.Name, jsonDatapoint.Probmethod_embedding, jsonDatapoint.SimilarityMethod, hash, entityId);
        Dictionary<string, float[]> embeddings = Datapoint.GetEmbeddings(jsonDatapoint.Text, [.. jsonDatapoint.Model], searchdomain.aIProvider, embeddingCache);
        var probMethod_embedding = new ProbMethod(jsonDatapoint.Probmethod_embedding) ?? throw new ProbMethodNotFoundException(jsonDatapoint.Probmethod_embedding);
        var similarityMethod = new SimilarityMethod(jsonDatapoint.SimilarityMethod) ?? throw new SimilarityMethodNotFoundException(jsonDatapoint.SimilarityMethod);
        return new Datapoint(jsonDatapoint.Name, probMethod_embedding, similarityMethod, hash, [.. embeddings.Select(kv => (kv.Key, kv.Value))]);
    }

    public static (Searchdomain?, int?, string?) TryGetSearchdomain(SearchdomainManager searchdomainManager, string searchdomain, ILogger logger)
    {
        try
        {
            Searchdomain searchdomain_ = searchdomainManager.GetSearchdomain(searchdomain);
            return (searchdomain_, null, null);
        } catch (SearchdomainNotFoundException)
        {
            logger.LogError("Unable to update searchdomain {searchdomain} - not found", [searchdomain]);
            return (null, 500, $"Unable to update searchdomain {searchdomain} - not found");
        } catch (Exception ex)
        {
            logger.LogError("Unable to update searchdomain {searchdomain} - Exception: {ex.Message} - {ex.StackTrace}", [searchdomain, ex.Message, ex.StackTrace]);
            return (null, 404, $"Unable to update searchdomain {searchdomain}");
        }
    }

    public static bool IsSearchdomainLoaded(SearchdomainManager searchdomainManager, string name)
    {
        return searchdomainManager.IsSearchdomainLoaded(name);
    }
}
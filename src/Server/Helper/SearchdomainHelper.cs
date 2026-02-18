using System.Collections.Concurrent;
using System.Diagnostics;
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

    public static bool CacheHasEntity(ConcurrentDictionary<string, Entity> entityCache, string name)
    {
        return CacheGetEntity(entityCache, name) is not null;
    }

    public static Entity? CacheGetEntity(ConcurrentDictionary<string, Entity> entityCache, string name)
    {
        foreach ((string _, Entity entity) in entityCache)
        {
            if (entity.name == name)
            {
                return entity;
            }
        }
        return null;
    }

    public async Task<List<Entity>?> EntitiesFromJSON(SearchdomainManager searchdomainManager, ILogger logger, string json)
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

        List<Task> entityTasks = [];
        foreach (JSONEntity jSONEntity in jsonEntities)
        {
            entityTasks.Add(Task.Run(async () =>
            {
                var entity = await EntityFromJSON(searchdomainManager, logger, jSONEntity);
                if (entity is not null)
                {
                    retVal.Enqueue(entity);
                }
            }));
            
            if (entityTasks.Count >= parallelOptions.MaxDegreeOfParallelism)
            {
                await Task.WhenAny(entityTasks);
                entityTasks.RemoveAll(t => t.IsCompleted);
            }
        }
        
        await Task.WhenAll(entityTasks);

        return [.. retVal];
    }

    public async Task<Entity?> EntityFromJSON(SearchdomainManager searchdomainManager, ILogger logger, JSONEntity jsonEntity)
    {
        var stopwatch = Stopwatch.StartNew();

        SQLHelper helper = searchdomainManager.helper;
        Searchdomain searchdomain = searchdomainManager.GetSearchdomain(jsonEntity.Searchdomain);
        int id_searchdomain = searchdomain.id;
        ConcurrentDictionary<string, Entity> entityCache = searchdomain.entityCache;
        AIProvider aIProvider = searchdomain.aIProvider;
        EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache = searchdomain.embeddingCache;
        bool invalidateSearchCache = false;


        bool hasEntity = entityCache.TryGetValue(jsonEntity.Name, out Entity? preexistingEntity);

        if (hasEntity && preexistingEntity is not null)
        {

            int preexistingEntityID = preexistingEntity.id;

            Dictionary<string, string> attributes = jsonEntity.Attributes;
            
            // Attribute - get changes
            List<(string attribute, string newValue, int entityId)> updatedAttributes = new(preexistingEntity.attributes.Count);
            List<(string attribute, int entityId)> deletedAttributes = new(preexistingEntity.attributes.Count);
            List<(string attributeKey, string attribute, int entityId)> addedAttributes = new(jsonEntity.Attributes.Count);
            foreach (KeyValuePair<string, string> attributesKV in preexistingEntity.attributes) //.ToList())
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

            if (updatedAttributes.Count != 0 || deletedAttributes.Count != 0 || addedAttributes.Count != 0)
                _logger.LogDebug("EntityFromJSON - Updating existing entity. name: {name}, updatedAttributes: {updatedAttributes}, deletedAttributes: {deletedAttributes}, addedAttributes: {addedAttributes}", [preexistingEntity.name, updatedAttributes.Count, deletedAttributes.Count, addedAttributes.Count]);
            // Attribute - apply changes
            if (updatedAttributes.Count != 0)
            {
                // Update
                await DatabaseHelper.DatabaseUpdateAttributes(helper, updatedAttributes);
                lock (preexistingEntity.attributes)
                {
                    updatedAttributes.ForEach(attribute => preexistingEntity.attributes[attribute.attribute] = attribute.newValue);
                }
            }
            if (deletedAttributes.Count != 0)
            {
                // Delete
                await DatabaseHelper.DatabaseDeleteAttributes(helper, deletedAttributes);
                lock (preexistingEntity.attributes)
                {
                    deletedAttributes.ForEach(attribute => preexistingEntity.attributes.Remove(attribute.attribute));
                }
            }
            if (addedAttributes.Count != 0)
            {
                // Insert
                await DatabaseHelper.DatabaseInsertAttributes(helper, addedAttributes);
                lock (preexistingEntity.attributes)
                {
                    addedAttributes.ForEach(attribute => preexistingEntity.attributes.Add(attribute.attributeKey, attribute.attribute));
                }
            }

            // Datapoint - get changes
            List<Datapoint> deletedDatapointInstances = new(preexistingEntity.datapoints.Count);
            List<string> deletedDatapoints = new(preexistingEntity.datapoints.Count);
            List<(string datapointName, int entityId, JSONDatapoint jsonDatapoint, string hash)> updatedDatapointsText = new(preexistingEntity.datapoints.Count);
            List<(string datapointName, string probMethod, string similarityMethod, int entityId, JSONDatapoint jsonDatapoint)> updatedDatapointsNonText = new(preexistingEntity.datapoints.Count);
            List<Datapoint> createdDatapointInstances = [];
            List<(string name, ProbMethodEnum probmethod_embedding, SimilarityMethodEnum similarityMethod, string hash, Dictionary<string, float[]> embeddings, JSONDatapoint datapoint)> createdDatapoints = new(jsonEntity.Datapoints.Length);
            
            foreach (Datapoint datapoint_ in preexistingEntity.datapoints.ToList())
            {
                Datapoint datapoint = datapoint_; // To enable replacing the datapoint reference as foreach iterators cannot be overwritten
                JSONDatapoint? newEntityDatapoint = jsonEntity.Datapoints.FirstOrDefault(x => x.Name == datapoint.name);
                bool newEntityHasDatapoint = newEntityDatapoint is not null;
                if (!newEntityHasDatapoint)
                {
                    // Datapoint - Deleted
                    deletedDatapointInstances.Add(datapoint);
                    deletedDatapoints.Add(datapoint.name);
                    invalidateSearchCache = true;
                } else
                {
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


            if (deletedDatapointInstances.Count != 0 || createdDatapoints.Count != 0 || addedAttributes.Count != 0 || updatedDatapointsNonText.Count != 0)
                _logger.LogDebug(
                    "EntityFromJSON - Updating existing entity. name: {name}, deletedDatapointInstances: {deletedDatapointInstances}, createdDatapoints: {createdDatapoints}, addedAttributes: {addedAttributes}, updatedDatapointsNonText: {updatedDatapointsNonText}",
                    [preexistingEntity.name, deletedDatapointInstances.Count, createdDatapoints.Count, addedAttributes.Count, updatedDatapointsNonText.Count]);
            // Datapoint - apply changes
            // Deleted
            if (deletedDatapointInstances.Count != 0)
            {
                await DatabaseHelper.DatabaseDeleteEmbeddingsAndDatapoints(helper, deletedDatapoints, (int)preexistingEntityID);
                preexistingEntity.datapoints = [.. preexistingEntity.datapoints
                    .Where(x =>
                        !deletedDatapointInstances.Contains(x)
                    )
                ];
            }
            // Created
            if (createdDatapoints.Count != 0)
            {
                List<Datapoint> datapoint = await DatabaseInsertDatapointsWithEmbeddings(helper, searchdomain, [.. createdDatapoints.Select(element => (element.datapoint, element.hash))], (int)preexistingEntityID, id_searchdomain);
                datapoint.ForEach(x => preexistingEntity.datapoints.Add(x));
            }
            // Datapoint - Updated (text)
            if (updatedDatapointsText.Count != 0)
            {
                await DatabaseHelper.DatabaseDeleteEmbeddingsAndDatapoints(helper, [.. updatedDatapointsText.Select(datapoint => datapoint.datapointName)], (int)preexistingEntityID);
                // Remove from datapoints
                var namesToRemove = updatedDatapointsText
                    .Select(d => d.datapointName)
                    .ToHashSet();
                var newBag = new ConcurrentBag<Datapoint>(
                    preexistingEntity.datapoints
                        .Where(x => !namesToRemove.Contains(x.name))
                );
                preexistingEntity.datapoints = newBag;
                // Insert into database
                List<Datapoint> datapoints = await DatabaseInsertDatapointsWithEmbeddings(helper, searchdomain, [.. updatedDatapointsText.Select(element => (datapoint: element.jsonDatapoint, hash: element.hash))], (int)preexistingEntityID, id_searchdomain);
                // Insert into datapoints
                datapoints.ForEach(datapoint => preexistingEntity.datapoints.Add(datapoint));
            }
            // Datapoint - Updated (probmethod or similaritymethod)
            if (updatedDatapointsNonText.Count != 0)
            {
                await DatabaseHelper.DatabaseUpdateDatapoint(
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
                searchdomain.UpdateModelsInUse();
            }

            return preexistingEntity;
        }
        else
        {
            int id_entity = await DatabaseHelper.DatabaseInsertEntity(helper, jsonEntity.Name, jsonEntity.Probmethod, id_searchdomain);
            List<(string attribute, string value, int id_entity)> toBeInsertedAttributes = [];
            foreach (KeyValuePair<string, string> attribute in jsonEntity.Attributes)
            {
                toBeInsertedAttributes.Add(new() {
                    attribute = attribute.Key,
                    value = attribute.Value,
                    id_entity = id_entity
                });
            }

            var insertAttributesTask = DatabaseHelper.DatabaseInsertAttributes(helper, toBeInsertedAttributes);

            List<(JSONDatapoint datapoint, string hash)> toBeInsertedDatapoints = [];
            ConcurrentBag<string> usedModels = searchdomain.modelsInUse;
            foreach (JSONDatapoint jsonDatapoint in jsonEntity.Datapoints)
            {
                string hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jsonDatapoint.Text)));
                toBeInsertedDatapoints.Add(new()
                {
                    datapoint = jsonDatapoint,
                    hash = hash
                });
                foreach (string model in jsonDatapoint.Model)
                {
                    if (!usedModels.Contains(model))
                    {
                        usedModels.Add(model);
                    }
                }
            }

            List<Datapoint> datapoints = await DatabaseInsertDatapointsWithEmbeddings(helper, searchdomain, toBeInsertedDatapoints, id_entity, id_searchdomain);
            
            var probMethod = Probmethods.GetMethod(jsonEntity.Probmethod) ?? throw new ProbMethodNotFoundException(jsonEntity.Probmethod);
            Entity entity = new(jsonEntity.Attributes, probMethod, jsonEntity.Probmethod.ToString(), new(datapoints), jsonEntity.Name)
            {
                id = id_entity
            };
            entityCache[jsonEntity.Name] = entity;

            searchdomain.ReconciliateOrInvalidateCacheForNewOrUpdatedEntity(entity);
            await insertAttributesTask;
            return entity;
        }
    }

    public async Task<List<Datapoint>> DatabaseInsertDatapointsWithEmbeddings(SQLHelper helper, Searchdomain searchdomain, List<(JSONDatapoint datapoint, string hash)> values, int id_entity, int id_searchdomain)
    {
        List<Datapoint> result = [];
        List<(string name, ProbMethodEnum probmethod_embedding, SimilarityMethodEnum similarityMethod, string hash)> toBeInsertedDatapoints = [];
        List<(int id_datapoint, string model, byte[] embedding)> toBeInsertedEmbeddings = [];
        foreach ((JSONDatapoint datapoint, string hash) value in values)
        {
            Datapoint datapoint = await BuildDatapointFromJsonDatapoint(value.datapoint, id_entity, searchdomain, value.hash);

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
                    id_datapoint = datapoint.id,
                    model = embedding.Item1,
                    embedding = BytesFromFloatArray(embedding.Item2)
                });
            }
            result.Add(datapoint);

        }
        
        await DatabaseHelper.DatabaseInsertEmbeddingBulk(helper, toBeInsertedEmbeddings, id_entity, id_searchdomain);
        return result;
    }

    public async Task<Datapoint> DatabaseInsertDatapointWithEmbeddings(SQLHelper helper, Searchdomain searchdomain, JSONDatapoint jsonDatapoint, int id_entity, int id_searchdomain, string? hash = null)
    {
        if (jsonDatapoint.Text is null)
        {
            throw new Exception("jsonDatapoint.Text must not be null at this point");
        }
        hash ??= GetHash(jsonDatapoint);
        Datapoint datapoint = await BuildDatapointFromJsonDatapoint(jsonDatapoint, id_entity, searchdomain, hash);
        int id_datapoint = await DatabaseHelper.DatabaseInsertDatapoint(helper, jsonDatapoint.Name, jsonDatapoint.Probmethod_embedding, jsonDatapoint.SimilarityMethod, hash, id_entity); // TODO make this a bulk add action to reduce number of queries
        List<(string model, byte[] embedding)> data = [];
        foreach ((string, float[]) embedding in datapoint.embeddings)
        {
            data.Add((embedding.Item1, BytesFromFloatArray(embedding.Item2)));
        }
        await DatabaseHelper.DatabaseInsertEmbeddingBulk(helper, id_datapoint, data, id_entity, id_searchdomain);
        return datapoint;
    }

    public string GetHash(JSONDatapoint jsonDatapoint)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jsonDatapoint.Text ?? throw new Exception("jsonDatapoint.Text must not be null to compute hash"))));
    }

    public async Task<Datapoint> BuildDatapointFromJsonDatapoint(JSONDatapoint jsonDatapoint, int entityId, Searchdomain searchdomain, string? hash = null)
    {
        if (jsonDatapoint.Text is null)
        {
            throw new Exception("jsonDatapoint.Text must not be null at this point");
        }
        SQLHelper helper = searchdomain.helper;
        EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache = searchdomain.embeddingCache;
        hash ??= Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jsonDatapoint.Text)));
        int id = await DatabaseHelper.DatabaseInsertDatapoint(helper, jsonDatapoint.Name, jsonDatapoint.Probmethod_embedding, jsonDatapoint.SimilarityMethod, hash, entityId);
        Dictionary<string, float[]> embeddings = Datapoint.GetEmbeddings(jsonDatapoint.Text, [.. jsonDatapoint.Model], searchdomain.aIProvider, embeddingCache);
        var probMethod_embedding = new ProbMethod(jsonDatapoint.Probmethod_embedding) ?? throw new ProbMethodNotFoundException(jsonDatapoint.Probmethod_embedding);
        var similarityMethod = new SimilarityMethod(jsonDatapoint.SimilarityMethod) ?? throw new SimilarityMethodNotFoundException(jsonDatapoint.SimilarityMethod);
        return new Datapoint(jsonDatapoint.Name, probMethod_embedding, similarityMethod, hash, [.. embeddings.Select(kv => (kv.Key, kv.Value))], id);
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
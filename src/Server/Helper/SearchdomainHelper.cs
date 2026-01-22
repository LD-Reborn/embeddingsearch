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

    public static bool CacheHasEntity(List<Entity> entityCache, string name)
    {
        return CacheGetEntity(entityCache, name) is not null;
    }

    public static Entity? CacheGetEntity(List<Entity> entityCache, string name)
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
        List<Entity> entityCache = searchdomain.entityCache;
        AIProvider aIProvider = searchdomain.aIProvider;
        EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache = searchdomain.embeddingCache;
        Entity? preexistingEntity = entityCache.FirstOrDefault(entity => entity.name == jsonEntity.Name);
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
            
            // Attribute
            foreach (KeyValuePair<string, string> attributesKV in preexistingEntity.attributes.ToList())
            {
                string oldAttributeKey = attributesKV.Key;
                string oldAttribute = attributesKV.Value;
                bool newHasAttribute = jsonEntity.Attributes.TryGetValue(oldAttributeKey, out string? newAttribute);
                if (newHasAttribute && newAttribute is not null && newAttribute != oldAttribute)
                {
                    // Attribute - Updated
                    Dictionary<string, dynamic> parameters = new()
                    {
                        { "newValue", newAttribute },
                        { "entityId", preexistingEntityID },
                        { "attribute", oldAttributeKey}
                    };
                    helper.ExecuteSQLNonQuery("UPDATE attribute SET value=@newValue WHERE id_entity=@entityId AND attribute=@attribute", parameters);
                    preexistingEntity.attributes[oldAttributeKey] = newAttribute;
                } else if (!newHasAttribute)
                {
                    // Attribute - Deleted
                    Dictionary<string, dynamic> parameters = new()
                    {
                        { "entityId", preexistingEntityID },
                        { "attribute", oldAttributeKey}
                    };
                    helper.ExecuteSQLNonQuery("DELETE FROM attribute WHERE id_entity=@entityId AND attribute=@attribute", parameters);
                    preexistingEntity.attributes.Remove(oldAttributeKey);
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
                    DatabaseHelper.DatabaseInsertAttribute(helper, newAttributeKey, newAttribute, (int)preexistingEntityID);
                    preexistingEntity.attributes.Add(newAttributeKey, newAttribute);
                }
            }

            // Datapoint
            foreach (Datapoint datapoint_ in preexistingEntity.datapoints.ToList())
            {
                Datapoint datapoint = datapoint_; // To enable replacing the datapoint reference as foreach iterators cannot be overwritten
                bool newEntityHasDatapoint = jsonEntity.Datapoints.Any(x => x.Name == datapoint.name);
                if (!newEntityHasDatapoint)
                {
                    // Datapoint - Deleted
                    Dictionary<string, dynamic> parameters = new()
                    {
                        { "datapointName", datapoint.name },
                        { "entityId", preexistingEntityID}
                    };
                    helper.ExecuteSQLNonQuery("DELETE e FROM embedding e JOIN datapoint d ON e.id_datapoint=d.id WHERE d.name=@datapointName AND d.id_entity=@entityId", parameters);
                    helper.ExecuteSQLNonQuery("DELETE FROM datapoint WHERE id_entity=@entityId AND name=@datapointName", parameters);
                    preexistingEntity.datapoints.Remove(datapoint);
                    invalidateSearchCache = true;
                } else
                {
                    JSONDatapoint? newEntityDatapoint = jsonEntity.Datapoints.FirstOrDefault(x => x.Name == datapoint.name);
                    if (newEntityDatapoint is not null && newEntityDatapoint.Text is not null)
                    {
                        // Datapoint - Updated (text)
                        Dictionary<string, dynamic> parameters = new()
                        {
                            { "datapointName", datapoint.name },
                            { "entityId", preexistingEntityID}
                        };
                        helper.ExecuteSQLNonQuery("DELETE e FROM embedding e JOIN datapoint d ON e.id_datapoint=d.id WHERE d.name=@datapointName AND d.id_entity=@entityId", parameters);
                        helper.ExecuteSQLNonQuery("DELETE FROM datapoint WHERE id_entity=@entityId AND name=@datapointName", parameters);
                        preexistingEntity.datapoints.Remove(datapoint);
                        Datapoint newDatapoint = DatabaseInsertDatapointWithEmbeddings(helper, searchdomain, newEntityDatapoint, (int)preexistingEntityID);
                        preexistingEntity.datapoints.Add(newDatapoint);
                        datapoint = newDatapoint;
                        invalidateSearchCache = true;
                    }
                    if (newEntityDatapoint is not null && (newEntityDatapoint.Probmethod_embedding != datapoint.probMethod.probMethodEnum || newEntityDatapoint.SimilarityMethod != datapoint.similarityMethod.similarityMethodEnum))
                    {
                        // Datapoint - Updated (probmethod or similaritymethod)
                        Dictionary<string, dynamic> parameters = new()
                        {
                            { "probmethod", newEntityDatapoint.Probmethod_embedding.ToString() },
                            { "similaritymethod", newEntityDatapoint.SimilarityMethod.ToString() },
                            { "datapointName", datapoint.name },
                            { "entityId", preexistingEntityID}
                        };
                        helper.ExecuteSQLNonQuery("UPDATE datapoint SET probmethod_embedding=@probmethod, similaritymethod=@similaritymethod WHERE id_entity=@entityId AND name=@datapointName", parameters);
                        Datapoint preexistingDatapoint = preexistingEntity.datapoints.First(x => x == datapoint); // The for loop is a copy. This retrieves the original such that it can be updated.
                        preexistingDatapoint.probMethod = new(newEntityDatapoint.Probmethod_embedding, _logger);
                        preexistingDatapoint.similarityMethod = new(newEntityDatapoint.SimilarityMethod, _logger);
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
                    Datapoint datapoint = DatabaseInsertDatapointWithEmbeddings(helper, searchdomain, jsonDatapoint, (int)preexistingEntityID);
                    preexistingEntity.datapoints.Add(datapoint);
                    invalidateSearchCache = true;
                }
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
            foreach (KeyValuePair<string, string> attribute in jsonEntity.Attributes)
            {
                DatabaseHelper.DatabaseInsertAttribute(helper, attribute.Key, attribute.Value, id_entity); // TODO implement bulk insert to reduce number of queries
            }

            List<Datapoint> datapoints = [];
            foreach (JSONDatapoint jsonDatapoint in jsonEntity.Datapoints)
            {
                string hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jsonDatapoint.Text)));
                Datapoint datapoint = DatabaseInsertDatapointWithEmbeddings(helper, searchdomain, jsonDatapoint, id_entity, hash);
                datapoints.Add(datapoint);
            }

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

    public Datapoint DatabaseInsertDatapointWithEmbeddings(SQLHelper helper, Searchdomain searchdomain, JSONDatapoint jsonDatapoint, int id_entity, string? hash = null)
    {
        if (jsonDatapoint.Text is null)
        {
            throw new Exception("jsonDatapoint.Text must not be null at this point");
        }
        hash ??= Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jsonDatapoint.Text)));
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
        var probMethod_embedding = new ProbMethod(jsonDatapoint.Probmethod_embedding, logger) ?? throw new ProbMethodNotFoundException(jsonDatapoint.Probmethod_embedding);
        var similarityMethod = new SimilarityMethod(jsonDatapoint.SimilarityMethod, logger) ?? throw new SimilarityMethodNotFoundException(jsonDatapoint.SimilarityMethod);
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
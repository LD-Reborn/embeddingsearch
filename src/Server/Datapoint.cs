using System.Collections.Concurrent;
using Shared;
using Shared.Models;

namespace Server;

public class Datapoint
{
    public string Name;
    public ProbMethod ProbMethod;
    public SimilarityMethod SimilarityMethod;
    public List<(string, float[])> Embeddings;
    public string Hash;
    public int Id;

    public Datapoint(string name, ProbMethodEnum probMethod, SimilarityMethodEnum similarityMethod, string hash, List<(string, float[])> embeddings, int id)
    {
        Name = name;
        ProbMethod = new ProbMethod(probMethod);
        SimilarityMethod = new SimilarityMethod(similarityMethod);
        Hash = hash;
        Embeddings = embeddings;
        Id = id;
    }

    public Datapoint(string name, ProbMethod probMethod, SimilarityMethod similarityMethod, string hash, List<(string, float[])> embeddings, int id)
    {
        Name = name;
        ProbMethod = probMethod;
        SimilarityMethod = similarityMethod;
        Hash = hash;
        Embeddings = embeddings;
        Id = id;
    }

    public float CalcProbability(List<(string, float)> probabilities)
    {
        return ProbMethod.Method(probabilities);
    }

    public static Dictionary<string, float[]> GetEmbeddings(string content, ConcurrentBag<string> models, AIProvider aIProvider, EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache)
    {
        Dictionary<string, float[]> embeddings = [];
        bool embeddingCacheHasContent = embeddingCache.TryGetValue(content, out var embeddingCacheForContent);
        if (!embeddingCacheHasContent || embeddingCacheForContent is null)
        {
            foreach (string model in models)
            {
                embeddings[model] = GenerateEmbeddings(content, model, aIProvider, embeddingCache);
            }
            return embeddings;
        }
        foreach (string model in models)
        {
            bool embeddingCacheHasModel = embeddingCacheForContent.TryGetValue(model, out float[]? embeddingCacheForModel);
            if (embeddingCacheHasModel && embeddingCacheForModel is not null)
            {
                embeddings[model] = embeddingCacheForModel;
            } else
            {
                embeddings[model] = GenerateEmbeddings(content, model, aIProvider, embeddingCache);
            }
        }
        return embeddings;
    }

    public static Dictionary<string, Dictionary<string, float[]>> GetEmbeddings(string[] content, List<string> models, AIProvider aIProvider, EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache)
    {
        Dictionary<string, Dictionary<string, float[]>> embeddings = [];
        foreach (string model in models)
        {
            List<string> toBeGenerated = [];
            embeddings[model] = [];
            foreach (string value in content)
            {
                bool generateThisEntry = true;
                bool embeddingCacheHasContent = embeddingCache.TryGetValue(value, out var embeddingCacheForContent);
                if (embeddingCacheHasContent && embeddingCacheForContent is not null)
                {
                    bool embeddingCacheHasModel = embeddingCacheForContent.TryGetValue(model, out float[]? embedding);
                    if (embeddingCacheHasModel && embedding is not null)
                    {
                        embeddings[model][value] = embedding;
                        generateThisEntry = false;
                    }
                }
                if (generateThisEntry)
                {
                    if (!toBeGenerated.Contains(value))
                    {
                        toBeGenerated.Add(value);
                    }
                }
            }
            if (toBeGenerated.Count == 0)
            {
                continue;
            }
            IEnumerable<float[]> generatedEmbeddings = GenerateEmbeddings([.. toBeGenerated], model, aIProvider, embeddingCache);
            if (generatedEmbeddings.Count() != toBeGenerated.Count)
            {
                throw new Exception("Requested embeddings count and generated embeddings count mismatched!");
            }
            for (int i = 0; i < toBeGenerated.Count; i++)
            {
                embeddings[model][toBeGenerated.ElementAt(i)] = generatedEmbeddings.ElementAt(i);
            }
        }
        return embeddings;
    }

    public static IEnumerable<float[]> GenerateEmbeddings(string[] content, string model, AIProvider aIProvider, EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache)
    {
        IEnumerable<float[]> embeddings = aIProvider.GenerateEmbeddings(model, content);
        if (embeddings.Count() != content.Length)
        {
            throw new Exception("Resulting embeddings count does not match up with request count");
        }
        for (int i = 0; i < content.Length; i++)
        {
            if (!embeddingCache.ContainsKey(content[i]))
            {
                embeddingCache[content[i]] = [];
            }
            embeddingCache[content[i]][model] = embeddings.ElementAt(i);
        }
        return embeddings;
    }


    public static float[] GenerateEmbeddings(string content, string model, AIProvider aIProvider, EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache)
    {
        float[] embeddings = aIProvider.GenerateEmbeddings(model, content);
        if (!embeddingCache.ContainsKey(content))
        {
            embeddingCache[content] = [];
        }
        embeddingCache[content][model] = embeddings;
        return embeddings;
    }
}
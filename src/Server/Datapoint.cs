using AdaptiveExpressions;
using OllamaSharp;
using OllamaSharp.Models;
using Shared;

namespace Server;

public class Datapoint
{
    public string name;
    public ProbMethod probMethod;
    public SimilarityMethod similarityMethod;
    public List<(string, float[])> embeddings;
    public string hash;

    public Datapoint(string name, ProbMethod probMethod, SimilarityMethod similarityMethod, string hash, List<(string, float[])> embeddings)
    {
        this.name = name;
        this.probMethod = probMethod;
        this.similarityMethod = similarityMethod;
        this.hash = hash;
        this.embeddings = embeddings;
    }

    public float CalcProbability(List<(string, float)> probabilities)
    {
        return probMethod.method(probabilities);
    }

    public static Dictionary<string, float[]> GetEmbeddings(string content, List<string> models, AIProvider aIProvider, EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache)
    {
        Dictionary<string, float[]> embeddings = [];
        bool embeddingCacheHasContent = embeddingCache.TryGetValue(content, out var embeddingCacheForContent);
        if (!embeddingCacheHasContent || embeddingCacheForContent is null)
        {
            models.ForEach(model =>
                embeddings[model] = GenerateEmbeddings(content, model, aIProvider, embeddingCache)
            );
            return embeddings;
        }
        models.ForEach(model =>
        {
            bool embeddingCacheHasModel = embeddingCacheForContent.TryGetValue(model, out float[]? embeddingCacheForModel);
            if (embeddingCacheHasModel && embeddingCacheForModel is not null)
            {
                embeddings[model] = embeddingCacheForModel;
            } else
            {
                embeddings[model] = GenerateEmbeddings(content, model, aIProvider, embeddingCache);
            }
        });
        return embeddings;
    }

    public static float[] GenerateEmbeddings(string content, string model, AIProvider aIProvider, EnumerableLruCache<string, Dictionary<string, float[]>> embeddingCache)
    {
        float[] embeddings = aIProvider.GenerateEmbeddings(model, [content]);
        if (!embeddingCache.ContainsKey(content))
        {
            embeddingCache[content] = [];
        }
        embeddingCache[content][model] = embeddings;
        return embeddings;
    }
}
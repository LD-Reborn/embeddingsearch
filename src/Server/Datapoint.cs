using AdaptiveExpressions;
using OllamaSharp;
using OllamaSharp.Models;

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

    public static Dictionary<string, float[]> GenerateEmbeddings(string content, List<string> models, AIProvider aIProvider)
    {
        return GenerateEmbeddings(content, models, aIProvider, new());
    }

    public static Dictionary<string, float[]> GenerateEmbeddings(string content, List<string> models, AIProvider aIProvider, LRUCache<string, Dictionary<string, float[]>> embeddingCache)
    {
        Dictionary<string, float[]> retVal = [];
        foreach (string model in models)
        {
            bool embeddingCacheHasModel = embeddingCache.TryGet(model, out var embeddingCacheForModel);
            if (embeddingCacheHasModel && embeddingCacheForModel.ContainsKey(content))
            {
                retVal[model] = embeddingCacheForModel[content];
                continue;
            }
            var response = aIProvider.GenerateEmbeddings(model, [content]);
            if (response is not null)
            {
                retVal[model] = response;
                if (!embeddingCacheHasModel)
                {
                    embeddingCacheForModel = [];
                }
                if (!embeddingCacheForModel.ContainsKey(content))
                {
                    embeddingCacheForModel[content] = response;
                }
            }
        }
        return retVal;
    }
}
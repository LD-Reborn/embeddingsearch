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
        return GenerateEmbeddings(content, models, aIProvider, []);
    }

    public static Dictionary<string, float[]> GenerateEmbeddings(string content, List<string> models, AIProvider aIProvider, Dictionary<string, Dictionary<string, float[]>> embeddingCache)
    {
        Dictionary<string, float[]> retVal = [];
        foreach (string model in models)
        {
            if (embeddingCache.ContainsKey(model) && embeddingCache[model].ContainsKey(content))
            {
                retVal[model] = embeddingCache[model][content];
                continue;
            }
            var response = aIProvider.GenerateEmbeddings(model, [content]);
            if (response is not null)
            {
                retVal[model] = response;
                if (!embeddingCache.ContainsKey(model))
                {
                    embeddingCache[model] = [];
                }
                if (!embeddingCache[model].ContainsKey(content))
                {
                    embeddingCache[model][content] = response;
                }
            }
        }
        return retVal;
    }
}
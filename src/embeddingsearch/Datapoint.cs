using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;

namespace embeddingsearch;

public class Datapoint
{
    public string name;
    public Probmethods.probMethodDelegate probMethod;
    public List<(string, float[])> embeddings;

    public Datapoint(string name, Probmethods.probMethodDelegate probMethod, List<(string, float[])> embeddings)
    {
        this.name = name;
        this.probMethod = probMethod;
        this.embeddings = embeddings;
    }

    // public Datapoint(string name, Probmethods.probMethodDelegate probmethod, string content, List<string> models, OllamaApiClient ollama)
    // {
    //     this.name = name;
    //     this.probMethod = probmethod;
    //     embeddings = GenerateEmbeddings(content, models, ollama);
    // }

    // public float CalcProbability()
    // {
    //     return probMethod(embeddings); // <--- prob method is not used with the embeddings!
    // }

    public float CalcProbability(List<(string, float)> probabilities)
    {
        return probMethod(probabilities);
    }

    public static Dictionary<string, float[]> GenerateEmbeddings(string content, List<string> models, OllamaApiClient ollama)
    {
        Dictionary<string, float[]> retVal = [];
        foreach (string model in models)
        {
            EmbedRequest request = new()
            {
                Model = model,
                Input = [content]
            };
            
            var response = ollama.GenerateEmbeddingAsync(content, new EmbeddingGenerationOptions(){ModelId=model}).Result;
            if (response is not null)
            {
                float[] var = new float[response.Vector.Length];
                response.Vector.CopyTo(var);
                retVal[model] = var;
            }
        }
        return retVal;
    }
}
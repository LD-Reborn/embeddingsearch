using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using server;

namespace Server;

public class Datapoint
{
    public string name;
    public ProbMethod probMethod;
    public List<(string, float[])> embeddings;
    public string hash;

    public Datapoint(string name, ProbMethod probMethod, string hash, List<(string, float[])> embeddings)
    {
        this.name = name;
        this.probMethod = probMethod;
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

    public static Dictionary<string, float[]> GenerateEmbeddings(List<string> contents, string model, OllamaApiClient ollama, Dictionary<string, Dictionary<string, float[]>> embeddingCache)
    {
        Dictionary<string, float[]> retVal = [];

        List<string> remainingContents = new List<string>(contents);
        for (int i = contents.Count - 1; i >= 0; i--) // Compare against cache and remove accordingly
        {
            string content = contents[i];
            if (embeddingCache.ContainsKey(model) && embeddingCache[model].ContainsKey(content))
            {
                retVal[content] = embeddingCache[model][content];
                remainingContents.RemoveAt(i);
            }
        }
        if (remainingContents.Count == 0)
        {
            return retVal;
        } 

        EmbedRequest request = new()
        {
            Model = model,
            Input = remainingContents
        };

        EmbedResponse response = ollama.EmbedAsync(request).Result;
        for (int i = 0; i < response.Embeddings.Count; i++)
        {
            string content = remainingContents.ElementAt(i);
            float[] embeddings = response.Embeddings.ElementAt(i);
            retVal[content] = embeddings;
            if (!embeddingCache.ContainsKey(model))
            {
                embeddingCache[model] = [];
            }
            if (!embeddingCache[model].ContainsKey(content))
            {
                embeddingCache[model][content] = embeddings;
            }
        }

        return retVal;
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
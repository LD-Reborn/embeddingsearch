using System.Numerics.Tensors;
using System.Text.Json;

namespace Server;

public class ProbMethod
{
    public Probmethods.probMethodDelegate method;
    public string name;

    public ProbMethod(string name, ILogger logger)
    {
        this.name = name;
        Probmethods.probMethodDelegate? probMethod = Probmethods.GetMethod(name);
        if (probMethod is null)
        {
            logger.LogError("Unable to retrieve probMethod {name}", [name]);
            throw new Exception("Unable to retrieve probMethod");
        }
        method = probMethod;
    }
}

public static class Probmethods
{
    public delegate float probMethodProtoDelegate(List<(string, float)> list, string parameters);
    public delegate float probMethodDelegate(List<(string, float)> list);
    public static readonly Dictionary<string, probMethodProtoDelegate> probMethods;

    static Probmethods()
    {
        probMethods = new Dictionary<string, probMethodProtoDelegate>
        {
            ["Mean"] = Mean,
            ["HarmonicMean"] = HarmonicMean,
            ["QuadraticMean"] = QuadraticMean,
            ["GeometricMean"] = GeometricMean,
            ["ExtremeValuesEmphasisWeightedAverage"] = ExtremeValuesEmphasisWeightedAverage,
            ["EVEWAvg"] = ExtremeValuesEmphasisWeightedAverage,
            ["HighValueEmphasisWeightedAverage"] = HighValueEmphasisWeightedAverage,
            ["HVEWAvg"] = HighValueEmphasisWeightedAverage,
            ["LowValueEmphasisWeightedAverage"] = LowValueEmphasisWeightedAverage,
            ["LVEWAvg"] = LowValueEmphasisWeightedAverage,
            ["DictionaryWeightedAverage"] = DictionaryWeightedAverage
        };
    }

    public static probMethodDelegate? GetMethod(string name)
    {
        string methodName = name;
        string? jsonArg = "";

        // Detect if parameters are embedded
        int colonIndex = name.IndexOf(':');
        if (colonIndex != -1)
        {
            methodName = name[..colonIndex];
            jsonArg = name[(colonIndex + 1)..];
        }

        if (!probMethods.TryGetValue(methodName, out probMethodProtoDelegate? method))
        {
            return null;
        }
        return list => method(list, jsonArg);
    }

    public static float Mean(List<(string, float)> list, string __)
    {
        if (list.Count == 0) return 0;
        float sum = 0;
        foreach ((_, float value) in list)
        {
            sum += value;
        }
        return sum / list.Count;
    }

    public static float HarmonicMean(List<(string, float)> list, string _)
    {
        int n_T = list.Count;
        float[] nonzeros = [.. list.Select(t => t.Item2).Where(t => t != 0)];
        int n_nz = nonzeros.Length;
        if (n_nz == 0) return 0;

        float nzSum = nonzeros.Sum(x => 1 / x);
        return n_nz / nzSum * (n_nz / (float)n_T);
    }

    public static float QuadraticMean(List<(string, float)> list, string _)
    {
        float sum = 0;
        foreach (var (_, value) in list)
        {
            sum += value * value;
        }
        return (float)Math.Sqrt(sum / list.Count);
    }

    public static float GeometricMean(List<(string, float)> list, string __)
    {
        if (list.Count == 0) return 0;
        float product = 1;
        foreach ((_, float value) in list)
        {
            product *= value;
        }
        return (float)Math.Pow(product, 1f / list.Count);
    }

    public static float ExtremeValuesEmphasisWeightedAverage(List<(string, float)> list, string _)
    {
        float[] arr = [.. list.Select(x => x.Item2)];
        if (arr.Contains(1)) return 1;
        if (arr.Contains(0)) return 0;

        float f = 0, fm = 0;
        foreach (float x in arr)
        {
            f += x / (x * (1 - x));
            fm += 1 / (x * (1 - x));
        }
        return f / fm;
    }

    public static float HighValueEmphasisWeightedAverage(List<(string, float)> list, string _)
    {
        float[] arr = [.. list.Select(x => x.Item2)];
        if (arr.Contains(1)) return 1;

        float f = 0, fm = 0;
        foreach (float x in arr)
        {
            f += x / (1 - x);
            fm += 1 / (1 - x);
        }
        return f / fm;
    }

    public static float LowValueEmphasisWeightedAverage(List<(string, float)> list, string _)
    {
        float[] arr = [.. list.Select(x => x.Item2)];
        if (arr.Contains(0)) return 0;

        float f = 0, fm = 0;
        foreach (float x in arr)
        {
            f += 1;
            fm += 1 / x;
        }
        return f / fm;
    }

    public static float DictionaryWeightedAverage(List<(string, float)> list, string jsonValues)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, float>>(jsonValues)
                      ?? throw new Exception($"Unable to convert the string to a Dictionary<string,float>: {jsonValues}");

        float f = 0, fm = 0;
        foreach (var (key, value) in list)
        {
            float fact = 1;
            if (values.TryGetValue(key, out float factor))
            {
                fact *= factor;
            }
            f += fact * value;
            fm += fact;
        }
        return f / fm;
    }
}

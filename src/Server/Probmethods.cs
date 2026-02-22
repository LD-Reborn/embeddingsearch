using System.Text.Json;
using Server.Exceptions;
using Shared.Models;

namespace Server;

public class ProbMethod
{
    public Probmethods.ProbMethodDelegate Method;
    public ProbMethodEnum ProbMethodEnum;
    public string Name;

    public ProbMethod(ProbMethodEnum probMethodEnum)
    {
        this.ProbMethodEnum = probMethodEnum;
        this.Name = probMethodEnum.ToString();
        Probmethods.ProbMethodDelegate? probMethod = Probmethods.GetMethod(Name) ?? throw new ProbMethodNotFoundException(probMethodEnum);
        Method = probMethod;
    }
}


public static class Probmethods
{
    public delegate float ProbMethodProtoDelegate(List<(string, float)> list, string parameters);
    public delegate float ProbMethodDelegate(List<(string, float)> list);
    public static readonly Dictionary<ProbMethodEnum, ProbMethodProtoDelegate> ProbMethods;

    static Probmethods()
    {
        ProbMethods = new Dictionary<ProbMethodEnum, ProbMethodProtoDelegate>
        {
            [ProbMethodEnum.Mean] = Mean,
            [ProbMethodEnum.HarmonicMean] = HarmonicMean,
            [ProbMethodEnum.QuadraticMean] = QuadraticMean,
            [ProbMethodEnum.GeometricMean] = GeometricMean,
            [ProbMethodEnum.EVEWAvg] = ExtremeValuesEmphasisWeightedAverage,
            [ProbMethodEnum.HVEWAvg] = HighValueEmphasisWeightedAverage,
            [ProbMethodEnum.LVEWAvg] = LowValueEmphasisWeightedAverage,
            [ProbMethodEnum.DictionaryWeightedAverage] = DictionaryWeightedAverage
        };
    }

    public static ProbMethodDelegate? GetMethod(ProbMethodEnum probMethodEnum)
    {
        return GetMethod(probMethodEnum.ToString());
    }

    public static ProbMethodDelegate? GetMethod(string name)
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
        ProbMethodEnum probMethodEnum = (ProbMethodEnum)Enum.Parse(
            typeof(ProbMethodEnum),
            methodName
        );

        if (!ProbMethods.TryGetValue(probMethodEnum, out ProbMethodProtoDelegate? method))
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

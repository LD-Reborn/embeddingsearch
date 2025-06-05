

using System.Numerics.Tensors;

namespace Server;


public class Probmethods
{
    public delegate float probMethodDelegate(List<(string, float)> list);
    public Dictionary<string, probMethodDelegate> probMethods;

    public Probmethods(Dictionary<string, probMethodDelegate> probMethods)
    {
        this.probMethods = probMethods;
    }
    
    public Probmethods()
    {
        probMethods = [];
        probMethods["wavg"] = WavgList;
        probMethods["weighted_average"] = WavgList;
    }

    public probMethodDelegate? GetMethod(string name)
    {
        try
        {
            return probMethods[name];
        } catch (Exception)
        {
            return null;
        }
    }

    public static float Fact(float x)
    {
        return 1 / (1 - x);
    }

    public static float WavgList(List<(string, float)> list)
    {
        float[] arr = new float[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            arr[i] = list.ElementAt(i).Item2;
        }
        return Wavg(arr);
    }

    public static float Wavg(float[] arr)
    {
        if (arr.Contains(1))
        {
            return 1;
        }
        float f = 0;
        float fm = 0;
        for (int i = 0; i < arr.Length; i++)
        {
            float x = arr[i];
            f += Fact(x);
            fm += x * Fact(x);
        }
        return f / fm;
    }

    public static float Similarity(float[] vector1, float[] vector2)
    {
        return (float) TensorPrimitives.CosineSimilarity(vector1, vector2);
    }
}
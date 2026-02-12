using System.Numerics.Tensors;
using Shared.Models;

namespace Server;

public class SimilarityMethod
{
    public SimilarityMethods.similarityMethodDelegate method;
    public SimilarityMethodEnum similarityMethodEnum;
    public string name;

    public SimilarityMethod(SimilarityMethodEnum similarityMethodEnum)
    {
        this.similarityMethodEnum = similarityMethodEnum;
        this.name = similarityMethodEnum.ToString();
        SimilarityMethods.similarityMethodDelegate? probMethod = SimilarityMethods.GetMethod(name) ?? throw new Exception($"Unable to retrieve similarityMethod {name}");
        method = probMethod;
    }
}

public static class SimilarityMethods
{
    public delegate float similarityMethodProtoDelegate(float[] vector1, float[] vector2);
    public delegate float similarityMethodDelegate(float[] vector1, float[] vector2);
    public static readonly Dictionary<SimilarityMethodEnum, similarityMethodProtoDelegate> probMethods;

    static SimilarityMethods()
    {
        probMethods = new Dictionary<SimilarityMethodEnum, similarityMethodProtoDelegate>
        {
            [SimilarityMethodEnum.Cosine] = CosineSimilarity,
            [SimilarityMethodEnum.Euclidian] = EuclidianDistance,
            [SimilarityMethodEnum.Manhattan] = ManhattanDistance,
            [SimilarityMethodEnum.Pearson] = PearsonCorrelation
        };
    }

    public static similarityMethodDelegate? GetMethod(string name)
    {
        string methodName = name;

        SimilarityMethodEnum probMethodEnum = (SimilarityMethodEnum)Enum.Parse(
            typeof(SimilarityMethodEnum),
            methodName
        );

        if (!probMethods.TryGetValue(probMethodEnum, out similarityMethodProtoDelegate? method))
        {
            return null;
        }
        return (vector1, vector2) => method(vector1, vector2);
    }


    public static float CosineSimilarity(float[] vector1, float[] vector2)
    {
        return (TensorPrimitives.CosineSimilarity(vector1, vector2) + 1) / 2;
    }

    public static float EuclidianDistance(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
        {
            throw new ArgumentException("Unable to calculate Euclidian distance - Vectors must have the same length");
        }
        float sum = 0;
        for (int i = 0; i < vector1.Length; i++)
        {
            float diff = vector1[i] - vector2[i];
            sum += diff * diff;
        }
        return RationalRemap((float)Math.Sqrt(sum));
    }

    public static float ManhattanDistance(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            throw new ArgumentException("Unable to calculate Manhattan distance - Vectors must have the same length");

        float sum = 0;
        for (int i = 0; i < vector1.Length; i++)
        {
            sum += Math.Abs(vector1[i] - vector2[i]);
        }
        return RationalRemap(sum);
    }

    public static float PearsonCorrelation(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            throw new ArgumentException("Unable to calculate Pearson correlation - Vectors must have the same length");

        int n = vector1.Length;
        double sum1 = vector1.Sum();
        double sum2 = vector2.Sum();
        double sum1Sq = vector1.Select(x => x * x).Sum();
        double sum2Sq = vector2.Select(x => x * x).Sum();
        double pSum = vector1.Zip(vector2, (x, y) => x * y).Sum();

        double num = pSum - (sum1 * sum2 / n);
        double den = Math.Sqrt((sum1Sq - (sum1 * sum1) / n) * (sum2Sq - (sum2 * sum2) / n));

        return den == 0 ? 0 : (float)(num / den);
    }

    public static float RationalRemap(float x)
    {
        if (x == float.PositiveInfinity)
        {
            return 0;
        }
        return 1 / (1 + x);
    }
}

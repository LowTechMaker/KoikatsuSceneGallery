using System.Numerics;

namespace KoikatsuSceneGallery.Services;

internal static class ImageFingerprintComparison
{
    public static int HammingDistance(ulong a, ulong b) =>
        BitOperations.PopCount(a ^ b);

    public static float HistogramCorrelation(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;
        int n = a.Length;
        double sumA = 0, sumB = 0, sumAB = 0, sumA2 = 0, sumB2 = 0;
        for (int i = 0; i < n; i++)
        {
            sumA += a[i];
            sumB += b[i];
            sumAB += a[i] * b[i];
            sumA2 += a[i] * a[i];
            sumB2 += b[i] * b[i];
        }

        double numerator = n * sumAB - sumA * sumB;
        double denominator = Math.Sqrt((n * sumA2 - sumA * sumA) * (n * sumB2 - sumB * sumB));
        return denominator < 1e-10 ? 0f : (float)(numerator / denominator);
    }
}


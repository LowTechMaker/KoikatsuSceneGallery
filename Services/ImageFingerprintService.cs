using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using KoikatsuSceneGallery.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace KoikatsuSceneGallery.Services;

public readonly record struct ImageFingerprint(ulong PHash, float[] Histogram);

public static class ImageFingerprintService
{
    private const int HashSize = 32;
    private const int DctLow = 8;
    private const int HistBins = 16;
    private const int HistChannels = 3;

    public static async Task<ImageFingerprint?> ComputeAsync(string filePath, CancellationToken ct)
    {
        try
        {
            byte[] pngBytes;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                long pngSize = PngEmbeddedData.GetPngSize(fs);
                if (pngSize <= 0) return null;
                pngBytes = new byte[pngSize];
                fs.Position = 0;
                await fs.ReadExactlyAsync(pngBytes, ct).ConfigureAwait(false);
            }

            using var ms = new InMemoryRandomAccessStream();
            await ms.WriteAsync(pngBytes.AsBuffer()).AsTask(ct).ConfigureAwait(false);
            ms.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(ms).AsTask(ct).ConfigureAwait(false);
            var transform = new BitmapTransform
            {
                ScaledWidth = HashSize,
                ScaledHeight = HashSize,
                InterpolationMode = BitmapInterpolationMode.Linear,
            };

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);

            var pixels = pixelData.DetachPixelData();
            int pixelCount = HashSize * HashSize;

            var gray = ToGray(pixels, pixelCount);
            ulong hash = ComputePHash(gray);
            float[] histogram = ComputeHistogram(pixels, pixelCount);

            return new ImageFingerprint(hash, histogram);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private static double[] ToGray(byte[] bgra, int pixelCount)
    {
        var result = new double[pixelCount];
        for (int i = 0; i < pixelCount; i++)
        {
            int idx = i * 4;
            result[i] = 0.299 * bgra[idx + 2] + 0.587 * bgra[idx + 1] + 0.114 * bgra[idx];
        }
        return result;
    }

    private static ulong ComputePHash(double[] gray)
    {
        // Separable DCT: rows then columns
        var dctRows = new double[HashSize * HashSize];
        for (int y = 0; y < HashSize; y++)
        {
            for (int u = 0; u < DctLow; u++)
            {
                double sum = 0;
                for (int x = 0; x < HashSize; x++)
                    sum += gray[y * HashSize + x] * Math.Cos(Math.PI / HashSize * (x + 0.5) * u);
                dctRows[y * DctLow + u] = sum;
            }
        }

        var dct = new double[DctLow * DctLow];
        for (int u = 0; u < DctLow; u++)
        {
            for (int v = 0; v < DctLow; v++)
            {
                double sum = 0;
                for (int y = 0; y < HashSize; y++)
                    sum += dctRows[y * DctLow + u] * Math.Cos(Math.PI / HashSize * (y + 0.5) * v);
                dct[v * DctLow + u] = sum;
            }
        }

        // Exclude DC component, find median of remaining 63 coefficients
        var values = new double[DctLow * DctLow - 1];
        Array.Copy(dct, 1, values, 0, values.Length);
        Array.Sort(values);
        double median = values[values.Length / 2];

        ulong hash = 0;
        for (int i = 1; i < DctLow * DctLow; i++)
        {
            if (dct[i] > median)
                hash |= 1UL << (i - 1);
        }

        return hash;
    }

    private static float[] ComputeHistogram(byte[] bgra, int pixelCount)
    {
        var hist = new float[HistChannels * HistBins];
        for (int i = 0; i < pixelCount; i++)
        {
            int idx = i * 4;
            int b = bgra[idx] >> 4;
            int g = bgra[idx + 1] >> 4;
            int r = bgra[idx + 2] >> 4;
            hist[r]++;                    // R: bins 0..15
            hist[HistBins + g]++;         // G: bins 16..31
            hist[2 * HistBins + b]++;     // B: bins 32..47
        }

        if (pixelCount > 0)
        {
            float inv = 1f / pixelCount;
            for (int i = 0; i < hist.Length; i++)
                hist[i] *= inv;
        }

        return hist;
    }

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

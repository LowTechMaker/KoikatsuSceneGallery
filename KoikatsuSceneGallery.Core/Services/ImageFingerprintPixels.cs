namespace KoikatsuSceneGallery.Services;

// Input is the existing decoder's 32x32 premultiplied BGRA8 pixel buffer.
internal static class ImageFingerprintPixels
{
    internal const int HashSize = 32;
    private const int DctLow = 8;
    private const int HistBins = 16;
    private const int HistChannels = 3;

    internal static (ulong PHash, float[] Histogram) Compute(byte[] pixels)
    {
        int pixelCount = HashSize * HashSize;
        var gray = ToGray(pixels, pixelCount);
        ulong hash = ComputePHash(gray);
        float[] histogram = ComputeHistogram(pixels, pixelCount);
        return (hash, histogram);
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
}


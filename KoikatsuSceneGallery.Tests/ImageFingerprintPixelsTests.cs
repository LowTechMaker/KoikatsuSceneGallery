using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImageFingerprintPixelsTests
{
    // Captured from the original service's numeric methods before moving them.
    [Theory]
    [InlineData(1U, 0x4A01F87A28BCF3E8UL)]
    [InlineData(17U, 0x5E4C0EFCE242D8CCUL)]
    [InlineData(123456U, 0x043F5499376C5A1DUL)]
    public void FixedPixelsPreserveOriginalHash(uint seed, ulong expected)
    {
        var pixels = new byte[32 * 32 * 4];
        uint state = seed;
        for (int i = 0; i < pixels.Length; i++)
        {
            state = unchecked(state * 1664525 + 1013904223);
            pixels[i] = (byte)(state >> 24);
        }
        var original = pixels.ToArray();
        Assert.Equal(expected, ImageFingerprintPixels.Compute(pixels).PHash);
        Assert.Equal(original, pixels);
    }

    [Fact]
    public void BlackPixelsHaveZeroHashAndOneOccupiedBinPerChannel()
    {
        var result = ImageFingerprintPixels.Compute(new byte[32 * 32 * 4]);
        Assert.Equal(0UL, result.PHash);
        Assert.Equal(48, result.Histogram.Length);
        for (int i = 0; i < 48; i++) Assert.Equal(i % 16 == 0 ? 1f : 0f, result.Histogram[i]);
    }

    [Fact]
    public void BgraPixelsProduceNormalizedRgbChannelBins()
    {
        var pixels = new byte[32 * 32 * 4];
        for (int i = 0; i < 32 * 32; i++)
        {
            pixels[i * 4 + (i % 2 == 0 ? 0 : 2)] = 255;
            pixels[i * 4 + 3] = 255;
        }
        var histogram = ImageFingerprintPixels.Compute(pixels).Histogram;
        var expected = new float[48];
        expected[0] = expected[15] = expected[32] = expected[47] = 0.5f;
        expected[16] = 1f;
        Assert.Equal(expected, histogram);
    }

    [Fact]
    public void NumericStageDoesNotApplyAlphaASecondTime()
    {
        // Windows already supplies premultiplied colors; this stage reads RGB only.
        var first = new byte[32 * 32 * 4];
        for (int i = 0; i < first.Length; i++) first[i] = (byte)(i % 251);
        var second = first.ToArray();
        for (int i = 3; i < second.Length; i += 4) second[i] ^= 255;
        var a = ImageFingerprintPixels.Compute(first);
        var b = ImageFingerprintPixels.Compute(second);
        Assert.Equal(a.PHash, b.PHash);
        Assert.Equal(a.Histogram, b.Histogram);
    }
}

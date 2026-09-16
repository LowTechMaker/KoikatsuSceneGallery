using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.IntegrationTests;

public sealed class ImageFingerprintServiceTests
{
    // Generated 32x32 opaque black RGBA PNG; no external image or card fixture.
    private const string BlackPng = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAK0lEQVR42u3OIQEAAAwEoetfeovxBoGn6sYEBAQEBAQEBAQEBAQEBAS2gQe3tfwuWQd6sAAAAABJRU5ErkJggg==";

    private sealed class InputFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ksg-fingerprint-" + Guid.NewGuid().ToString("N") + ".png");
        public InputFile(byte[] data) => File.WriteAllBytes(Path, data);
        public void Dispose() => File.Delete(Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealWindowsDecoderFeedsCoreAndIgnoresCardTail(bool appendTail)
    {
        var bytes = Convert.FromBase64String(BlackPng);
        if (appendTail) bytes = [.. bytes, 100, 0, 0, 0, 255, 254, 253, 252];
        using var file = new InputFile(bytes);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await ImageFingerprintService.ComputeAsync(file.Path, deadline.Token).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.NotNull(result);
        Assert.Equal(0UL, result.Value.PHash);
        Assert.Equal(48, result.Value.Histogram.Length);
        for (int i = 0; i < 48; i++) Assert.Equal(i % 16 == 0 ? 1f : 0f, result.Value.Histogram[i]);
        Assert.Equal(bytes, File.ReadAllBytes(file.Path));
    }

    [Fact]
    public async Task RealDecoderPreservesRgbChannelMeaning()
    {
        const string redPng = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAK0lEQVR42u3OIQEAAAwEoetfeovxBoGnq1tKQEBAQEBAQEBAQEBAQEBgHXhUDfhqeP5ugAAAAABJRU5ErkJggg==";
        using var file = new InputFile(Convert.FromBase64String(redPng));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await ImageFingerprintService.ComputeAsync(file.Path, deadline.Token).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.NotNull(result);
        var expectedPixels = new byte[32 * 32 * 4];
        for (int i = 0; i < expectedPixels.Length; i += 4)
        {
            expectedPixels[i + 2] = 255; // BGRA red
            expectedPixels[i + 3] = 255;
        }
        var expected = ImageFingerprintPixels.Compute(expectedPixels);
        Assert.Equal(expected.PHash, result.Value.PHash);
        Assert.Equal(expected.Histogram, result.Value.Histogram);
        Assert.Equal(1f, result.Value.Histogram[15]); // red channel, highest bin
        Assert.Equal(0f, result.Value.Histogram[47]); // blue channel, highest bin
    }

    [Fact]
    public async Task InvalidPngReturnsNull()
    {
        using var file = new InputFile([1, 2, 3, 4]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Null(await ImageFingerprintService.ComputeAsync(file.Path, deadline.Token).WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public async Task CancellationIsNotConvertedToMissingFingerprint()
    {
        using var file = new InputFile(Convert.FromBase64String(BlackPng));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ImageFingerprintService.ComputeAsync(file.Path, new CancellationToken(true)).WaitAsync(TimeSpan.FromSeconds(15)));
    }
}

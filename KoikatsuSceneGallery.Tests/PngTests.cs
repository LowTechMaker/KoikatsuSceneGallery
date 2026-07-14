using System.Buffers.Binary;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class PngTests
{
    [Fact]
    public void ReadDimensions_ReturnsIhdrDimensions()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("中文_日本語.png", TestFiles.Png(1920, 1080));

        Assert.Equal((1920, 1080), PngHelper.ReadDimensions(path));
    }

    [Fact]
    public void ReadDimensions_ShortFileReturnsZeros()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("short.png", new byte[23]);

        Assert.Equal((0, 0), PngHelper.ReadDimensions(path));
    }

    [Fact]
    public void ReadDimensions_DoesNotValidatePngSignature()
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), 12);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), 34);
        using var directory = new TestDirectory();
        var path = directory.Write("not-a-png.bin", bytes);

        Assert.Equal((12, 34), PngHelper.ReadDimensions(path));
    }

    [Fact]
    public void GetPngSize_ReturnsLeadingImageLengthAndRestoresPosition()
    {
        var prefixed = new byte[3 + TestFiles.PngLength + 2];
        TestFiles.Png().CopyTo(prefixed, 3);
        using var stream = new MemoryStream(prefixed) { Position = 3 };

        Assert.Equal(TestFiles.PngLength, PngEmbeddedData.GetPngSize(stream));
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void GetPngSize_InvalidSignatureReturnsZeroAndRestoresPosition()
    {
        using var stream = new MemoryStream(new byte[32]) { Position = 4 };

        Assert.Equal(0, PngEmbeddedData.GetPngSize(stream));
        Assert.Equal(4, stream.Position);
    }

    [Fact]
    public void GetPngSize_TruncatedChunkReturnsZero()
    {
        var truncated = TestFiles.Png()[..^1];
        using var stream = new MemoryStream(truncated);

        Assert.Equal(0, PngEmbeddedData.GetPngSize(stream));
        Assert.Equal(0, stream.Position);
    }
}

using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using MessagePack;

namespace KoikatsuSceneGallery.Tests;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KoikatsuSceneGallery.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Write(string relativePath, ReadOnlySpan<byte> bytes)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes.ToArray());
        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

internal static class TestFiles
{
    public const int PngLength = 45;

    public static byte[] Png(int width = 1, int height = 1, ReadOnlySpan<byte> appended = default)
    {
        var bytes = new byte[PngLength + appended.Length];
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        signature.CopyTo(bytes, 0);

        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        bytes[24] = 8;
        bytes[25] = 6;

        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(33, 4), 0);
        "IEND"u8.CopyTo(bytes.AsSpan(37, 4));
        appended.CopyTo(bytes.AsSpan(PngLength));
        return bytes;
    }

    public static byte[] BinaryCard(string marker, string version, string? coordinateName = null)
    {
        using var stream = new MemoryStream();
        stream.Write(Png());
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(100);
        writer.Write(marker);
        writer.Write(version);
        if (coordinateName is not null)
            writer.Write(coordinateName);
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] CharacterCard(
        string marker = "KoiKatuChara",
        string? lastName = "姓",
        string? firstName = "名",
        string? nickname = "暱稱",
        int sex = 1,
        string? pluginKey = "madevil.example")
    {
        byte[] parameter = Parameter(lastName, firstName, nickname, sex);
        byte[] kkex = Kkex(pluginKey);

        byte[] header = BlockHeader(("Parameter", 0, parameter.Length), ("KKEx", parameter.Length, kkex.Length));
        byte[] data = [.. parameter, .. kkex];

        using var stream = new MemoryStream();
        stream.Write(Png());
        using var binaryWriter = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        binaryWriter.Write(100);
        binaryWriter.Write(marker);
        binaryWriter.Write("1.0.0");
        binaryWriter.Write(0);
        binaryWriter.Write(header.Length);
        binaryWriter.Write(header);
        binaryWriter.Write((long)data.Length);
        binaryWriter.Write(data);
        binaryWriter.Flush();
        return stream.ToArray();
    }

    private static byte[] Parameter(string? lastName, string? firstName, string? nickname, int sex)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(4);
        writer.Write("lastname");
        writer.Write(lastName);
        writer.Write("firstname");
        writer.Write(firstName);
        writer.Write("nickname");
        writer.Write(nickname);
        writer.Write("sex");
        writer.Write(sex);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] Kkex(string? pluginKey)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(pluginKey is null ? 0 : 1);
        if (pluginKey is not null)
        {
            writer.Write(pluginKey);
            writer.Write(1);
        }
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BlockHeader(params (string Name, long Position, long Size)[] blocks)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(1);
        writer.Write("lstInfo");
        writer.WriteArrayHeader(blocks.Length);
        foreach (var block in blocks)
        {
            writer.WriteMapHeader(3);
            writer.Write("name");
            writer.Write(block.Name);
            writer.Write("pos");
            writer.Write(block.Position);
            writer.Write("size");
            writer.Write(block.Size);
        }
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }
}

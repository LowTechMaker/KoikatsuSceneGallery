using System.Text;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using MessagePack;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Parses a Koikatsu character card (.png). Structure after the leading preview
/// PNG (located via <see cref="PngEmbeddedData.GetPngSize"/>):
///   int productNo · string marker · string version · int faceLen + face PNG ·
///   int blockHeaderLen + BlockHeader · long dataLen + data.
/// The BlockHeader (MessagePack) lists named blocks with their position/size in
/// the data region. We read the <c>Parameter</c> block (name + sex) and the
/// <c>KKEx</c> block's top-level plugin GUID keys (Madevil detection). KKEx values
/// are skipped with <see cref="MessagePackReader.Skip()"/> — never materialized —
/// so even a 32 MB KKEx block is traversed cheaply (mirrors SceneMetadataParser).
/// Never throws; returns null on anything unrecognized.
/// </summary>
public static class CharacterCardParser
{
    private const string MadevilGuidPrefix = "madevil.";
    private const string SunshineMarker = "KoiKatuCharaSun";
    private const string KoikatsuMarker = "KoiKatuChara";

    private readonly record struct BlockInfo(string Name, long Pos, long Size);

    public static CharacterMetadata? TryParse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

            long pngSize = PngEmbeddedData.GetPngSize(fs);
            if (pngSize <= 0) return null;
            fs.Seek(pngSize, SeekOrigin.Begin);

            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
            _ = br.ReadInt32();                 // productNo
            string marker = br.ReadString();
            var game = marker.Contains(SunshineMarker, StringComparison.Ordinal) ? GameVersion.KoikatsuSunshine
                : marker.Contains(KoikatsuMarker, StringComparison.Ordinal) ? GameVersion.Koikatsu
                : GameVersion.Unknown;
            if (game == GameVersion.Unknown) return null;

            _ = br.ReadString();                // card version
            int faceLen = br.ReadInt32();
            if (faceLen < 0 || faceLen > fs.Length - fs.Position) return null;
            fs.Seek(faceLen, SeekOrigin.Current);

            int blockHeaderLen = br.ReadInt32();
            if (blockHeaderLen <= 0 || blockHeaderLen > fs.Length - fs.Position) return null;
            byte[] headerBytes = br.ReadBytes(blockHeaderLen);
            if (headerBytes.Length != blockHeaderLen) return null;

            long dataLen = br.ReadInt64();
            long dataStart = fs.Position;
            if (dataLen <= 0 || dataLen > fs.Length - dataStart) return null;

            var blocks = ReadBlockHeader(headerBytes);

            string? last = null, first = null, nick = null;
            int sex = -1;
            var param = blocks.FirstOrDefault(b => b.Name == "Parameter");
            if (param.Name == "Parameter" && param.Size > 0 && param.Size <= dataLen - param.Pos)
            {
                byte[] pbytes = ReadBlock(fs, dataStart + param.Pos, (int)param.Size);
                ReadParameter(pbytes, out last, out first, out nick, out sex);
            }

            bool isMadevil = false;
            var kkex = blocks.FirstOrDefault(b => b.Name == "KKEx");
            if (kkex.Name == "KKEx" && kkex.Size > 0 && kkex.Size <= dataLen - kkex.Pos)
            {
                byte[] kbytes = ReadBlock(fs, dataStart + kkex.Pos, (int)kkex.Size);
                isMadevil = ReadTopLevelKeys(kbytes)
                    .Any(k => k.StartsWith(MadevilGuidPrefix, StringComparison.OrdinalIgnoreCase));
            }

            return new CharacterMetadata(last, first, nick, sex, game, isMadevil);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] ReadBlock(Stream stream, long offset, int size)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        byte[] buffer = new byte[size];
        stream.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>Reads the BlockHeader map's "lstInfo" array into block descriptors.</summary>
    private static List<BlockInfo> ReadBlockHeader(byte[] headerBytes)
    {
        var blocks = new List<BlockInfo>();
        var reader = new MessagePackReader(headerBytes);
        int mapCount = reader.ReadMapHeader();
        for (int i = 0; i < mapCount; i++)
        {
            string? key = reader.ReadString();
            if (key != "lstInfo")
            {
                reader.Skip();
                continue;
            }

            int arrCount = reader.ReadArrayHeader();
            for (int j = 0; j < arrCount; j++)
            {
                string name = string.Empty;
                long pos = 0, size = 0;
                int fieldCount = reader.ReadMapHeader();
                for (int f = 0; f < fieldCount; f++)
                {
                    switch (reader.ReadString())
                    {
                        case "name": name = reader.ReadString() ?? string.Empty; break;
                        case "pos": pos = reader.ReadInt64(); break;
                        case "size": size = reader.ReadInt64(); break;
                        default: reader.Skip(); break;
                    }
                }
                blocks.Add(new BlockInfo(name, pos, size));
            }
        }
        return blocks;
    }

    private static void ReadParameter(byte[] bytes, out string? last, out string? first, out string? nick, out int sex)
    {
        last = first = nick = null;
        sex = -1;
        var reader = new MessagePackReader(bytes);
        int count = reader.ReadMapHeader();
        for (int i = 0; i < count; i++)
        {
            switch (reader.ReadString())
            {
                case "lastname": last = reader.ReadString(); break;
                case "firstname": first = reader.ReadString(); break;
                case "nickname": nick = reader.ReadString(); break;
                case "sex": sex = reader.ReadInt32(); break;
                default: reader.Skip(); break;
            }
        }
    }

    private static List<string> ReadTopLevelKeys(byte[] blob)
    {
        var reader = new MessagePackReader(blob);
        int count = reader.ReadMapHeader();
        var keys = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            keys.Add(reader.ReadString() ?? string.Empty);
            reader.Skip();
        }
        return keys;
    }
}

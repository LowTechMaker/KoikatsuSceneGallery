using System.Text;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using MessagePack;

namespace KoikatsuSceneGallery.Services;

/// <summary>Read-only KK/Party/KKS card metadata. No plugin code is loaded or executed.</summary>
public static class CardMetadataReader
{
    public static CardMetadataDocument? TryRead(string path, bool includeExtendedData = true, CancellationToken token = default)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long pngEnd = PngEmbeddedData.GetPngSize(stream);
            if (pngEnd <= 0) return null;
            stream.Position = pngEnd;
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            _ = reader.ReadInt32();
            string marker = ReadString(reader);
            string cardType = marker.Trim('【', '】') switch
            {
                "KoiKatuChara" => "Koikatu",
                "KoiKatuCharaS" => "KoikatsuParty",
                "KoiKatuCharaSP" => "KoikatsuPartySpecialPatch",
                "KoiKatuCharaSun" => "KoikatsuSunshine",
                "KoiKatuClothes" => "KoikatuClothes",
                _ => "Unknown"
            };
            // Preserve the existing coordinate reader's permissive marker matching.
            if (cardType == "Unknown" && marker.Contains("KoiKatuClothes", StringComparison.Ordinal)) cardType = "KoikatuClothes";
            if (cardType == "Unknown") return null;
            string version = ReadString(reader);
            string? coordinateName = null;
            var blocks = new List<MetadataBlock>();
            var guids = new List<string>();
            var plugins = new List<PluginMetadataSummary>();
            if (cardType == "KoikatuClothes")
            {
                coordinateName = ReadString(reader);
                // Older/basic fixtures can end after the name. No extended data is valid.
                if (stream.Position < stream.Length)
                {
                    try
                    {
                        Skip(stream, reader.ReadInt32());
                        if (stream.Position < stream.Length)
                        {
                            if (ReadString(reader) != "KKEx") throw new InvalidDataException("InvalidExtendedMarker");
                            _ = reader.ReadInt32();
                            long length = reader.ReadInt32();
                            blocks.Add(ReadBlock(stream, "KKEx", stream.Position, length, includeExtendedData, guids, plugins, token));
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) when (IsDataError(ex)) { blocks.Add(new("KKEx", 0, "Unavailable", null, "InvalidCoordinateTail")); }
                }
            }
            else
            {
                Skip(stream, reader.ReadInt32()); // embedded face PNG
                var header = MetadataValueReader.Read(ReadBytes(reader, reader.ReadInt32()), token);
                long dataLength = reader.ReadInt64();
                long dataStart = stream.Position;
                if (dataLength <= 0 || dataLength > stream.Length - dataStart) return null;
                var descriptors = header.Field("lstInfo")?.Items ?? throw new InvalidDataException("MissingBlockTable");
                foreach (var entry in descriptors)
                {
                    token.ThrowIfCancellationRequested();
                    string? name = entry.Field("name")?.Text();
                    if (name is not ("Parameter" or "About" or "KKEx")) continue;
                    long pos = entry.Field("pos")?.Integer() ?? -1;
                    long size = entry.Field("size")?.Integer() ?? -1;
                    if (pos < 0 || size <= 0 || pos > dataLength || size > dataLength - pos)
                        blocks.Add(new(name, Math.Max(0, size), "Unavailable", null, "InvalidBlockRange"));
                    else
                        blocks.Add(ReadBlock(stream, name, dataStart + pos, size, includeExtendedData, guids, plugins, token));
                }
            }

            var parameter = blocks.FirstOrDefault(b => b.Name == "Parameter")?.Data;
            var about = blocks.FirstOrDefault(b => b.Name == "About")?.Data;
            var last = parameter?.Field("lastname")?.Text();
            var first = parameter?.Field("firstname")?.Text();
            bool sunshine = cardType == "KoikatsuSunshine";
            string? aboutVersion = about?.Field("version")?.Text();
            var summary = new CardMetadataSummary
            {
                CardType = cardType,
                // KKManager normalizes supported About versions through ComplementWithVersion.
                Version = sunshine && about is not null && Version.TryParse(aboutVersion, out var av) && av <= new Version(0, 0, 0) ? "0.0.0" : version,
                Name = coordinateName ?? string.Join(" ", new[] { last, first }.Where(x => !string.IsNullOrWhiteSpace(x))),
                LastName = last, FirstName = first, Nickname = parameter?.Field("nickname")?.Text(),
                Sex = IntField(parameter, "sex") is 0 or 1 ? IntField(parameter, "sex")!.Value : -1,
                PersonalityId = IntField(parameter, "personality"),
                Language = sunshine ? IntField(about, "language") : null,
                UserId = sunshine ? about?.Field("userID")?.Text() : null,
                DataId = sunshine ? about?.Field("dataID")?.Text() : null,
                Game = sunshine ? GameVersion.KoikatsuSunshine : cardType == "KoikatuClothes" ? GameVersion.Unknown : GameVersion.Koikatsu,
                PluginGuids = guids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                Plugins = plugins.ToArray(),
                ExtendedSize = blocks.Where(b => b.Name == "KKEx").Sum(b => b.Size),
                IsPartial = blocks.Any(b => b.Status == "Unavailable")
            };
            return new CardMetadataDocument(Path.GetFullPath(path), stream.Length, marker, summary, blocks) { HeaderVersion = version };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (IsDataError(ex)) { return null; }
    }

    private static int? IntField(MetadataValue? node, string key)
        => node?.Field(key)?.Integer() is long n && n is >= int.MinValue and <= int.MaxValue ? (int)n : null;

    private static MetadataBlock ReadBlock(Stream stream, string name, long pos, long size, bool full, List<string> guids, List<PluginMetadataSummary> plugins, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (pos < 0 || size < 0 || pos > stream.Length || size > stream.Length - pos) throw new InvalidDataException("InvalidBlockRange");
            if (size > MetadataValueReader.MaxBlockBytes) return new(name, size, "Unavailable", null, "BlockSizeLimit");
            stream.Position = pos;
            byte[] bytes = new byte[(int)size];
            stream.ReadExactly(bytes);
            if (name == "KKEx")
            {
                var r = new MessagePackReader(bytes);
                int count = r.ReadMapHeader();
                if (count > MetadataValueReader.MaxNodes) throw new InvalidDataException("ExpansionLimit");
                int nodes = 0;
                for (int i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var key = r.ReadString();
                    if (key is not null) guids.Add(key);
                    var preview = r;
                    int? version = null;
                    if (preview.NextMessagePackType == MessagePackType.Array && preview.ReadArrayHeader() >= 1
                        && preview.NextMessagePackType == MessagePackType.Integer)
                        version = preview.ReadInt32();
                    long start = r.Consumed;
                    MetadataValueReader.Skip(ref r, 1, ref nodes, token);
                    if (key is not null) plugins.Add(new(key, version, r.Consumed - start));
                }
                if (!r.End) throw new InvalidDataException("TrailingData");
                if (!full) return new(name, size, "Summary", null);
            }
            var data = MetadataValueReader.Read(bytes, token);
            if (data.Kind != "map") throw new InvalidDataException("ExpectedMap");
            return new(name, size, "Complete", data);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (IsDataError(ex))
        {
            return new(name, Math.Max(0, size), "Unavailable", null, ex is InvalidDataException ? ex.Message : "InvalidMessagePack");
        }
    }

    private static byte[] ReadBytes(BinaryReader reader, int size)
    {
        if (size <= 0 || size > MetadataValueReader.MaxBlockBytes || size > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("InvalidLength");
        var bytes = reader.ReadBytes(size);
        if (bytes.Length != size) throw new EndOfStreamException();
        return bytes;
    }
    private static string ReadString(BinaryReader reader)
    {
        int length = reader.Read7BitEncodedInt();
        if (length < 0 || length > 1024 * 1024 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("InvalidStringLength");
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }
    private static void Skip(Stream stream, long bytes)
    {
        if (bytes < 0 || bytes > stream.Length - stream.Position) throw new InvalidDataException("InvalidLength");
        stream.Seek(bytes, SeekOrigin.Current);
    }
    private static bool IsDataError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
        or InvalidOperationException or OverflowException or FormatException or MessagePackSerializationException;
}

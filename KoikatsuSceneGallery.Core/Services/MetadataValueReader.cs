using System.Buffers;
using System.Globalization;
using KoikatsuSceneGallery.Models;
using MessagePack;

namespace KoikatsuSceneGallery.Services;

internal static class MetadataValueReader
{
    internal const int MaxBlockBytes = 64 * 1024 * 1024;
    internal const int MaxNodes = 100_000;
    internal static void Skip(ref MessagePackReader r, int depth, ref int nodes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (depth > 64 || ++nodes > MaxNodes) throw new InvalidDataException("ExpansionLimit");
        if (r.NextMessagePackType == MessagePackType.Array)
        {
            int count = r.ReadArrayHeader();
            if (count > MaxNodes - nodes) throw new InvalidDataException("ExpansionLimit");
            for (int i = 0; i < count; i++) Skip(ref r, depth + 1, ref nodes, token);
        }
        else if (r.NextMessagePackType == MessagePackType.Map)
        {
            int count = r.ReadMapHeader();
            if (count > (MaxNodes - nodes) / 2) throw new InvalidDataException("ExpansionLimit");
            for (int i = 0; i < count; i++) { Skip(ref r, depth + 1, ref nodes, token); Skip(ref r, depth + 1, ref nodes, token); }
        }
        else r.Skip();
    }
    public static MetadataValue Read(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length > MaxBlockBytes) throw new InvalidDataException("BlockSizeLimit");
        var reader = new MessagePackReader(bytes);
        int nodes = 0;
        var value = Read(ref reader, 0, ref nodes, token);
        if (!reader.End) throw new InvalidDataException("TrailingData");
        return value;
    }

    private static MetadataValue Read(ref MessagePackReader r, int depth, ref int nodes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (depth > 64 || ++nodes > MaxNodes) throw new InvalidDataException("ExpansionLimit");
        switch (r.NextMessagePackType)
        {
            case MessagePackType.Nil: r.ReadNil(); return new("nil");
            case MessagePackType.Boolean: return new("boolean", r.ReadBoolean() ? "true" : "false");
            case MessagePackType.Integer:
                bool unsigned = r.NextCode <= 0x7f || r.NextCode is >= 0xcc and <= 0xcf;
                return unsigned ? new("unsigned", r.ReadUInt64().ToString(CultureInfo.InvariantCulture))
                    : new("integer", r.ReadInt64().ToString(CultureInfo.InvariantCulture));
            case MessagePackType.Float:
                return r.NextCode == 0xca ? new("float32", r.ReadSingle().ToString("R", CultureInfo.InvariantCulture))
                    : new("float64", r.ReadDouble().ToString("R", CultureInfo.InvariantCulture));
            case MessagePackType.String: return new("string", r.ReadString());
            case MessagePackType.Binary: return new("binary", Convert.ToBase64String(r.ReadBytes()!.Value.ToArray()));
            case MessagePackType.Extension:
                var ext = r.ReadExtensionFormat();
                return new("extension", Convert.ToBase64String(ext.Data.ToArray()), TypeCode: ext.TypeCode);
            case MessagePackType.Array:
                int count = r.ReadArrayHeader();
                if (count > MaxNodes - nodes) throw new InvalidDataException("ExpansionLimit");
                var items = new List<MetadataValue>(count);
                for (int i = 0; i < count; i++) items.Add(Read(ref r, depth + 1, ref nodes, token));
                return new("array", Items: items);
            case MessagePackType.Map:
                int pairs = r.ReadMapHeader();
                if (pairs > (MaxNodes - nodes) / 2) throw new InvalidDataException("ExpansionLimit");
                var entries = new List<MetadataEntry>(pairs);
                for (int i = 0; i < pairs; i++) entries.Add(new(Read(ref r, depth + 1, ref nodes, token), Read(ref r, depth + 1, ref nodes, token)));
                return new("map", Entries: entries);
            default: throw new InvalidDataException("UnsupportedValue");
        }
    }
}

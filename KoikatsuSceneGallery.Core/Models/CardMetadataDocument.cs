using System.Globalization;
using System.Text.Json.Serialization;

namespace KoikatsuSceneGallery.Models;

public sealed record CardMetadataSummary
{
    public string CardType { get; init; } = "Unknown";
    public string? Version { get; init; }
    public string? Name { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Nickname { get; init; }
    public int Sex { get; init; } = -1;
    public int? PersonalityId { get; init; }
    public int? Language { get; init; }
    public string? UserId { get; init; }
    public string? DataId { get; init; }
    public GameVersion Game { get; init; }
    public string[] PluginGuids { get; init; } = [];
    public PluginMetadataSummary[] Plugins { get; init; } = [];
    public long ExtendedSize { get; init; }
    public bool IsPartial { get; init; }
    [JsonIgnore] public bool IsMadevil => PluginGuids.Any(x => x.StartsWith("madevil.", StringComparison.OrdinalIgnoreCase));
}

public sealed record PluginMetadataSummary(string Guid, int? Version, long Size);

// Explicit value kinds preserve MessagePack types without CLR type activation.
// Integers are decimal strings so JSON consumers cannot silently round UInt64 values.
public sealed record MetadataValue(string Kind, string? Value = null,
    IReadOnlyList<MetadataValue>? Items = null, IReadOnlyList<MetadataEntry>? Entries = null,
    sbyte? TypeCode = null)
{
    public MetadataValue? Field(string key) => Entries?.FirstOrDefault(x => x.Key.Kind == "string" && x.Key.Value == key)?.Value;
    public string? Text() => Kind == "string" ? Value : null;
    public long? Integer() => Kind is "integer" or "unsigned" && long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
public sealed record MetadataEntry(MetadataValue Key, MetadataValue Value);
public sealed record MetadataBlock(string Name, long Size, string Status, MetadataValue? Data, string? Diagnostic = null);
public sealed record CardMetadataDocument(string FileName, long FileSize, string Marker,
    CardMetadataSummary Summary, IReadOnlyList<MetadataBlock> Blocks)
{
    public int SchemaVersion => 1;
    public string? HeaderVersion { get; init; }
}

using System.Security.Cryptography;
using System.Text.Json;

namespace KoikatsuSceneGallery.Services;

/// <summary>The post a card came from, as KoikatsuSceneFeed recorded it.</summary>
/// <param name="PostUrl">The post's address; an installed plugin may use it to canonicalize the artwork id.</param>
/// <param name="CreatorId">The provider's creator id, when Feed recorded one.</param>
/// <param name="CreatorName">The creator's display name, when Feed recorded one.</param>
public sealed record FeedHandoffSource(
    string ProviderId,
    string PostId,
    string? PostUrl = null,
    string? CreatorId = null,
    string? CreatorName = null);

/// <summary>
/// Reads KoikatsuSceneFeed's hand-off manifest, <c>&lt;Feed Downloads&gt;\.scenefeed\handoff.json</c>, which names
/// the post each downloaded card came from by the card's SHA-256. A card out of an archive or a file host has a
/// name that says nothing about its post, so this is the only way to identify it without review. The format is
/// documented in KoikatsuSceneFeed's <c>docs/handoff-manifest.md</c>.
/// </summary>
/// <remarks>
/// Anything unexpected — no manifest, an unknown format, a newer version, a broken file — reads as "no manifest",
/// and the card is identified (or not) exactly as before. A manifest is parsed once and read again only after Feed
/// rewrites it.
/// </remarks>
public sealed class FeedHandoffManifest
{
    public const string Format = "scenefeed-handoff";
    public const int Version = 1;
    private const string FolderName = ".scenefeed";
    private const string FileName = "handoff.json";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (DateTime WrittenAt, long Length, IReadOnlyDictionary<string, FeedHandoffSource> Files)> _manifests =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The post the card at <paramref name="cardPath"/> came from, or null when no manifest above it names it.</summary>
    public FeedHandoffSource? Find(string cardPath) => Find(FilesFor(cardPath), cardPath);

    /// <summary>
    /// The post the card at <paramref name="cardPath"/> came from, by the manifest of the Feed folder
    /// <paramref name="manifestDirectory"/> (the folder holding <c>.scenefeed</c>), wherever the card is now.
    /// </summary>
    /// <remarks>
    /// For a card that has already left Feed's Downloads, such as one in the library: there is no manifest above it
    /// any more, but Feed's still names it by its bytes. Only that one manifest is read.
    /// </remarks>
    public FeedHandoffSource? Find(string manifestDirectory, string cardPath)
        => Find(Cached(new FileInfo(Path.Combine(Path.GetFullPath(manifestDirectory), FolderName, FileName))), cardPath);

    private static FeedHandoffSource? Find(IReadOnlyDictionary<string, FeedHandoffSource>? files, string cardPath)
    {
        if (files is null || files.Count == 0)
            return null;

        string sha;
        try
        {
            using var stream = File.OpenRead(cardPath);
            sha = Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return files.GetValueOrDefault(sha);
    }

    private IReadOnlyDictionary<string, FeedHandoffSource>? FilesFor(string cardPath)
    {
        for (var folder = Path.GetDirectoryName(Path.GetFullPath(cardPath)); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
        {
            if (Cached(new FileInfo(Path.Combine(folder, FolderName, FileName))) is { } files)
                return files;
        }

        return null;
    }

    /// <summary>The manifest's entries, parsed once and again only after Feed rewrites it; null when there is none.</summary>
    private IReadOnlyDictionary<string, FeedHandoffSource>? Cached(FileInfo manifest)
    {
        if (!manifest.Exists)
            return null;

        lock (_gate)
        {
            if (_manifests.TryGetValue(manifest.FullName, out var cached)
                && cached.WrittenAt == manifest.LastWriteTimeUtc && cached.Length == manifest.Length)
                return cached.Files;

            var files = Read(manifest.FullName);
            _manifests[manifest.FullName] = (manifest.LastWriteTimeUtc, manifest.Length, files);
            return files;
        }
    }

    private static IReadOnlyDictionary<string, FeedHandoffSource> Read(string path)
    {
        var result = new Dictionary<string, FeedHandoffSource>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != Format
                || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var v) || v < 1 || v > Version
                || !root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var file in files.EnumerateArray())
            {
                if (file.ValueKind != JsonValueKind.Object
                    || Text(file, "sha256") is not { } sha
                    || !file.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
                    continue;

                var first = sources.EnumerateArray()
                    .Select(s => s.ValueKind == JsonValueKind.Object && Text(s, "providerId") is { } provider && Text(s, "postId") is { } post
                        ? new FeedHandoffSource(
                            provider,
                            post,
                            Text(s, "postUrl"),
                            Text(s, "creatorId"),
                            Text(s, "creatorName"))
                        : null)
                    .FirstOrDefault(s => s is not null);
                if (first is not null)
                    result.TryAdd(sha, first);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            result.Clear();
        }

        return result;
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
}

using System.Collections.Concurrent;
using System.Text.Json;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Stores fetched artwork metadata next to its author folder.  The sidecar is
/// deliberately outside the app cache so it travels with an imported library.
/// </summary>
internal sealed class PostMetadataStore
{
    public const string MetadataDirectoryName = ".scenegallery";
    public const string FetchedDataDirectoryName = "fetched_data";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteLocks =
        new(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    public string GetSidecarPath(
        string authorDirectory,
        string providerId,
        string artworkId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artworkId);

        var fileName =
            $"{Uri.EscapeDataString(providerId)}_{Uri.EscapeDataString(artworkId)}.json";
        return Path.Combine(
            Path.GetFullPath(authorDirectory),
            MetadataDirectoryName,
            FetchedDataDirectoryName,
            fileName);
    }

    public async Task<bool> WriteAsync(
        string authorDirectory,
        PostMetadataDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);

        var path = GetSidecarPath(
            authorDirectory,
            document.ProviderId,
            document.ArtworkId);
        var writeLock = WriteLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        string? temporaryPath = null;
        try
        {
            var existing = ReadFile(path);
            if (existing is not null && existing.FetchedAt >= document.FetchedAt)
                return false;

            var metadataDirectory = Path.Combine(
                Path.GetFullPath(authorDirectory),
                MetadataDirectoryName);
            var fetchedDataDirectory = Path.Combine(
                metadataDirectory,
                FetchedDataDirectoryName);
            Directory.CreateDirectory(fetchedDataDirectory);
            MarkHiddenOnWindows(metadataDirectory);

            temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
            temporaryPath = null;
            return true;
        }
        finally
        {
            try
            {
                if (temporaryPath is not null)
                    File.Delete(temporaryPath);
            }
            finally
            {
                writeLock.Release();
            }
        }
    }

    public IReadOnlyList<PostMetadataDocument> ReadAll(string authorDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorDirectory);

        var directory = Path.Combine(
            Path.GetFullPath(authorDirectory),
            MetadataDirectoryName,
            FetchedDataDirectoryName);
        if (!Directory.Exists(directory))
            return [];

        var documents = new List<PostMetadataDocument>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var document = ReadFile(path);
            if (document is not null)
                documents.Add(document);
        }

        return documents;
    }

    public PostMetadataDocument? Read(
        string authorDirectory,
        string providerId,
        string artworkId)
    {
        var document = ReadFile(GetSidecarPath(authorDirectory, providerId, artworkId));
        return document is not null
            && string.Equals(document.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(document.ArtworkId, artworkId, StringComparison.OrdinalIgnoreCase)
            ? document
            : null;
    }

    public bool Delete(string authorDirectory, string providerId, string artworkId)
    {
        var path = GetSidecarPath(authorDirectory, providerId, artworkId);
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        return true;
    }

    private static PostMetadataDocument? ReadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<PostMetadataDocument>(stream, JsonOptions);
            return document is not null && IsValid(document) ? document : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void Validate(PostMetadataDocument document)
    {
        if (!IsValid(document))
            throw new ArgumentException("The post metadata document is invalid.", nameof(document));
    }

    private static bool IsValid(PostMetadataDocument document)
        => document.SchemaVersion == PostMetadataDocument.CurrentSchemaVersion
            && !string.IsNullOrWhiteSpace(document.ProviderId)
            && !string.IsNullOrWhiteSpace(document.ArtworkId)
            && !string.IsNullOrWhiteSpace(document.AuthorName)
            && !string.IsNullOrWhiteSpace(document.AuthorId)
            && document.Rating is >= 0 and <= 2
            && document.Tags is not null
            && document.Tags.All(static tag =>
                tag is not null && !string.IsNullOrWhiteSpace(tag.Name))
            && document.FetchedAt != default;

    private static void MarkHiddenOnWindows(string path)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Hidden) == 0)
            File.SetAttributes(path, attributes | FileAttributes.Hidden);
    }
}

using System.Text.Json;
using KoikatsuSceneGallery.Models;

using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

internal sealed record SidecarWriteReceipt(
    string SidecarFilePath,
    string ProviderId,
    string ArtworkId,
    IReadOnlyList<string> NewlyAddedFileNames,
    bool SidecarCreatedByThisImport,
    bool WasWritten);

internal enum SidecarUnbindResult
{
    SidecarNotFound,
    FileNameNotFound,
    FileNameRemoved,
    SidecarDeleted,
}

/// <summary>
/// Stores fetched artwork metadata next to its author folder.  The sidecar is
/// deliberately outside the app cache so it travels with an imported library.
/// </summary>
internal sealed class PostMetadataStore
{
    public const string MetadataDirectoryName = LibraryMetadataDirectory.Name;
    public const string FetchedDataDirectoryName = "fetched_data";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

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
        => (await WriteWithReceiptAsync(authorDirectory, document, cancellationToken)
            .ConfigureAwait(false)).WasWritten;

    /// <summary>
    /// Writes or merges fetched metadata and reports the local file-name mutation made by this call.
    /// </summary>
    public Task<SidecarWriteReceipt> WriteWithReceiptAsync(
        string authorDirectory,
        PostMetadataDocument document,
        CancellationToken cancellationToken = default)
        => ExecuteWriteCoreAsync(authorDirectory, document, cancellationToken);

    /// <summary>
    /// Removes one local file-name association without changing fetched metadata.
    /// </summary>
    public async Task<SidecarUnbindResult> RemoveLocalFileNameAsync(
        string authorDirectory,
        string providerId,
        string artworkId,
        string fileName,
        bool shouldDeleteIfEmpty,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var path = GetSidecarPath(authorDirectory, providerId, artworkId);
        var writeLock = PathWriteLock.For(path);
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!File.Exists(path))
                return SidecarUnbindResult.SidecarNotFound;

            var document = ReadFile(path);
            if (document is null)
                return SidecarUnbindResult.SidecarNotFound;

            var updatedFileNames = document.LocalFileNames
                .Where(existing => !existing.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (updatedFileNames.Count == document.LocalFileNames.Count)
                return SidecarUnbindResult.FileNameNotFound;

            if (updatedFileNames.Count == 0 && shouldDeleteIfEmpty)
            {
                File.Delete(path);
                return SidecarUnbindResult.SidecarDeleted;
            }

            await WriteDocumentAtomicallyAsync(
                path,
                document with { LocalFileNames = updatedFileNames },
                cancellationToken).ConfigureAwait(false);
            return SidecarUnbindResult.FileNameRemoved;
        }
        finally
        {
            writeLock.Release();
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

    private async Task<SidecarWriteReceipt> ExecuteWriteCoreAsync(
        string authorDirectory,
        PostMetadataDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);

        var path = GetSidecarPath(
            authorDirectory,
            document.ProviderId,
            document.ArtworkId);
        var writeLock = PathWriteLock.For(path);
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var sidecarCreatedByThisImport = !File.Exists(path);
            var existing = ReadFile(path);
            var newlyAddedFileNames = existing is null
                ? DistinctNonEmptyFileNames(document.LocalFileNames)
                : GetNewlyAddedFileNames(existing.LocalFileNames, document.LocalFileNames);

            if (existing is not null)
            {
                // Artwork pages can be imported at different times. Preserve
                // every known local filename even when the cached metadata is
                // newer than the incoming document.
                var mergedFileNames = MergeLocalFileNames(
                    existing.LocalFileNames,
                    document.LocalFileNames);

                if (existing.FetchedAt >= document.FetchedAt)
                {
                    if (mergedFileNames.Count == existing.LocalFileNames.Count)
                    {
                        return new SidecarWriteReceipt(
                            path,
                            document.ProviderId,
                            document.ArtworkId,
                            [],
                            sidecarCreatedByThisImport,
                            WasWritten: false);
                    }

                    document = existing with
                    {
                        SchemaVersion = PostMetadataDocument.CurrentSchemaVersion,
                        LocalFileNames = mergedFileNames,
                    };
                }
                else
                {
                    document = document with { LocalFileNames = mergedFileNames };
                }
            }

            var metadataDirectory = Path.Combine(
                Path.GetFullPath(authorDirectory),
                MetadataDirectoryName);
            var fetchedDataDirectory = Path.Combine(
                metadataDirectory,
                FetchedDataDirectoryName);
            Directory.CreateDirectory(fetchedDataDirectory);
            LibraryMetadataDirectory.MarkHiddenOnWindows(metadataDirectory);

            await WriteDocumentAtomicallyAsync(path, document, cancellationToken).ConfigureAwait(false);
            return new SidecarWriteReceipt(
                path,
                document.ProviderId,
                document.ArtworkId,
                newlyAddedFileNames,
                sidecarCreatedByThisImport,
                WasWritten: true);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static Task WriteDocumentAtomicallyAsync(
        string path,
        PostMetadataDocument document,
        CancellationToken cancellationToken)
        => AtomicJsonFile.WriteAsync(path, document, JsonOptions, cancellationToken);

    private static PostMetadataDocument? ReadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<PostMetadataDocument>(stream, JsonOptions);
            // Version 1 predates LocalFileNames. System.Text.Json may leave
            // absent init properties null, so normalize it before validation.
            if (document is not null && document.LocalFileNames is null)
                document = document with { LocalFileNames = [] };
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

    private static List<string> MergeLocalFileNames(
        IReadOnlyList<string> existing,
        IReadOnlyList<string> added)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>(existing.Count + added.Count);
        foreach (var fileName in existing.Concat(added))
        {
            if (!string.IsNullOrWhiteSpace(fileName) && seen.Add(fileName))
                merged.Add(fileName);
        }

        return merged;
    }

    private static IReadOnlyList<string> GetNewlyAddedFileNames(
        IReadOnlyList<string> existing,
        IReadOnlyList<string> incoming)
    {
        var known = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return DistinctNonEmptyFileNames(incoming.Where(known.Add));
    }

    private static IReadOnlyList<string> DistinctNonEmptyFileNames(IEnumerable<string> fileNames)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. fileNames.Where(fileName =>
            !string.IsNullOrWhiteSpace(fileName) && seen.Add(fileName))];
    }

    private static bool IsValid(PostMetadataDocument document)
        => document.SchemaVersion is >= 1 and <= PostMetadataDocument.CurrentSchemaVersion
            && !string.IsNullOrWhiteSpace(document.ProviderId)
            && !string.IsNullOrWhiteSpace(document.ArtworkId)
            && !string.IsNullOrWhiteSpace(document.AuthorName)
            && !string.IsNullOrWhiteSpace(document.AuthorId)
            && document.Rating is >= 0 and <= 2
            && document.Tags is not null
            && document.LocalFileNames is not null
            && document.Tags.All(static tag =>
                tag is not null && !string.IsNullOrWhiteSpace(tag.Name))
            && document.FetchedAt != default;

}

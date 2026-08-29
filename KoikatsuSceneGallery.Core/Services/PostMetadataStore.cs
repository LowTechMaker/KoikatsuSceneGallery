using System.Text.Json;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

internal sealed class PostMetadataStore
{
    public const string MetadataDirectoryName = ".scenegallery";
    public const string FetchedDataDirectoryName = "fetched_data";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// A per-sidecar write lock together with the number of callers that hold
    /// a reference to it. Both fields are guarded by <see cref="WriteLocksGate"/>.
    /// </summary>
    private sealed class WriteLockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }

    private static readonly object WriteLocksGate = new();

    private static readonly Dictionary<string, WriteLockEntry> WriteLocks =
        new(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    /// <summary>
    /// Number of live write locks. Exposed for tests, which assert that the
    /// dictionary does not grow with the number of sidecars written.
    /// </summary>
    internal static int ActiveWriteLockCount
    {
        get
        {
            lock (WriteLocksGate)
                return WriteLocks.Count;
        }
    }

    /// <summary>
    /// Number of callers currently referencing the lock for a path. Exposed so
    /// tests can observe a waiter taking and dropping its reference.
    /// </summary>
    internal static int GetWriteLockReferenceCount(string path)
    {
        lock (WriteLocksGate)
            return WriteLocks.TryGetValue(path, out var entry) ? entry.ReferenceCount : 0;
    }

    /// <summary>
    /// Holds the same per-path write lock <see cref="WriteAsync"/> uses, so a
    /// test can block a writer deterministically. Uses the production
    /// acquire/release sequence rather than reimplementing it.
    /// </summary>
    internal static async Task<IDisposable> HoldWriteLockAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var entry = AcquireWriteLock(path);
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseWriteLock(path, entry);
            throw;
        }

        return new WriteLockHolder(path, entry);
    }

    private sealed class WriteLockHolder(string path, WriteLockEntry entry)
        : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            entry.Semaphore.Release();
            ReleaseWriteLock(path, entry);
        }
    }

    /// <summary>
    /// Takes a reference to the lock for <paramref name="path"/>, creating it
    /// when needed. Taking the reference under the gate is what makes the
    /// removal in <see cref="ReleaseWriteLock"/> safe: an entry can only be
    /// removed while no caller holds a reference to it, so a later caller
    /// cannot end up with a second semaphore for a path someone else is
    /// still writing.
    /// </summary>
    private static WriteLockEntry AcquireWriteLock(string path)
    {
        lock (WriteLocksGate)
        {
            if (!WriteLocks.TryGetValue(path, out var entry))
            {
                entry = new WriteLockEntry();
                WriteLocks[path] = entry;
            }

            entry.ReferenceCount++;
            return entry;
        }
    }

    /// <summary>
    /// Drops a reference and disposes the semaphore once the last one is gone.
    /// The caller must already have released the semaphore itself.
    /// </summary>
    private static void ReleaseWriteLock(string path, WriteLockEntry entry)
    {
        lock (WriteLocksGate)
        {
            if (--entry.ReferenceCount > 0)
                return;

            WriteLocks.Remove(path);
            entry.Semaphore.Dispose();
        }
    }

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
        var writeLock = AcquireWriteLock(path);
        try
        {
            await writeLock.Semaphore.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // The semaphore was never entered — drop the reference only.
            ReleaseWriteLock(path, writeLock);
            throw;
        }

        string? temporaryPath = null;
        try
        {
            var existing = ReadFile(path);
            if (existing is not null)
            {
                // Importing another page of the same artwork brings only that
                // page's file names, so the recorded names have to accumulate.
                // Losing them would leave the earlier files unmatchable.
                var mergedFileNames = MergeLocalFileNames(
                    existing.LocalFileNames,
                    document.LocalFileNames);

                if (existing.FetchedAt >= document.FetchedAt)
                {
                    // The stored metadata is the fresher one and must win, but
                    // the new file names are still worth persisting.
                    if (mergedFileNames.Count == existing.LocalFileNames.Count)
                        return false;

                    // The rewrite adds fields the stored schema version may not
                    // know about, so it is stamped with the current version.
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
                writeLock.Semaphore.Release();
                ReleaseWriteLock(path, writeLock);
            }
        }
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

    public bool Delete(
        string authorDirectory,
        string providerId,
        string artworkId)
    {
        var path = GetSidecarPath(authorDirectory, providerId, artworkId);
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        return true;
    }

    /// <summary>
    /// Unions the recorded file names, keeping the order already stored so an
    /// unchanged sidecar is recognised by its name count.
    /// </summary>
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

    private static PostMetadataDocument? ReadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<PostMetadataDocument>(stream, JsonOptions);
        // Normalize v1 sidecars that predate LocalFileNames — System.Text.Json
        // leaves missing init properties as null, not the initializer value.
        if (document is not null && document.LocalFileNames is null)
            document = document with { LocalFileNames = [] };
        return document is not null && IsValid(document)
            ? document
            : null;
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

    private static void MarkHiddenOnWindows(string path)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Hidden) == 0)
            File.SetAttributes(path, attributes | FileAttributes.Hidden);
    }
}

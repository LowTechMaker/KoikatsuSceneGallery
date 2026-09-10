using System.Collections.Concurrent;
using System.Text.Json;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Stores what the user recorded about character cards — which version a card
/// is, a note, and which character it belongs to — beside the cards
/// themselves.
/// </summary>
/// <remarks>
/// Like <see cref="PostMetadataStore"/> and <see cref="LocalSourceStore"/>
/// this lives under <c>.scenegallery</c> so it travels with the library. It
/// deliberately does not go in the metadata cache: that is keyed by path and
/// modification time, so touching a file would silently discard everything the
/// user typed.
///
/// Reads are cached per directory. The scan calls this once per card, and a
/// library has thousands of cards across dozens of directories, so reading per
/// card would add thousands of file opens to the scan path.
/// </remarks>
internal class CharacterAnnotationStore
{
    public const string DocumentFileName = "characters.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteLocks =
        new(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, CharacterAnnotationEntry> Empty =
        new Dictionary<string, CharacterAnnotationEntry>(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, CharacterAnnotationEntry>>> _cache =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public string GetDocumentPath(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        return Path.Combine(
            Path.GetFullPath(directory),
            PostMetadataStore.MetadataDirectoryName,
            DocumentFileName);
    }

    /// <summary>
    /// The annotation for one card, or null when it has none. Safe to call on
    /// the scan path: at most one file read per directory.
    /// </summary>
    public CharacterAnnotationEntry? GetForCard(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
            return null;

        return LoadDirectory(directory).GetValueOrDefault(Path.GetFileName(filePath));
    }

    /// <summary>
    /// Records the user's annotation for one card. An annotation that says
    /// nothing is removed, and a document with no entries left is deleted, so
    /// clearing an annotation leaves no trace behind.
    /// </summary>
    public async Task<CharacterAnnotationEntry?> UpdateAsync(
        string filePath,
        CharacterVersionKind kind,
        bool superseded,
        string? note,
        string? groupKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
            return null;

        var fileName = Path.GetFileName(filePath);
        var documentPath = GetDocumentPath(directory);
        var writeLock = WriteLocks.GetOrAdd(documentPath, static _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var now = DateTimeOffset.UtcNow;
            var existing = ReadDocument(documentPath);
            var cards = new Dictionary<string, CharacterAnnotationEntry>(
                existing?.Cards ?? Empty,
                StringComparer.OrdinalIgnoreCase);

            var entry = new CharacterAnnotationEntry
            {
                Label = kind.ToToken(),
                Superseded = superseded,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                GroupKey = string.IsNullOrWhiteSpace(groupKey) ? null : groupKey.Trim(),
                FileSize = SafeFileSize(filePath),
                UpdatedAt = now,
            };

            if (entry.IsEmpty)
                cards.Remove(fileName);
            else
                cards[fileName] = entry;

            if (cards.Count == 0)
            {
                DeleteDocument(documentPath);
                _cache[Path.GetFullPath(directory)] = Lazy(Empty);
                return null;
            }

            var document = new CharacterAnnotationDocument(CharacterAnnotationDocument.CurrentSchemaVersion)
            {
                Cards = cards,
                CreatedAt = existing?.CreatedAt is { } created && created != default ? created : now,
                UpdatedAt = now,
            };

            var metadataDirectory = Path.GetDirectoryName(documentPath)!;
            Directory.CreateDirectory(metadataDirectory);
            MarkHiddenOnWindows(metadataDirectory);
            await WriteDocumentAtomicallyAsync(documentPath, document, cancellationToken).ConfigureAwait(false);

            // The app is the only writer, so the cache can be updated in place
            // rather than re-read.
            _cache[Path.GetFullPath(directory)] = Lazy(cards);
            return entry.IsEmpty ? null : entry;
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    /// Drops every cached directory. Called when the gallery reloads, so an
    /// edit made outside the app is picked up.
    /// </summary>
    public void ClearCache() => _cache.Clear();

    internal void InvalidateDirectory(string directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
            _cache.TryRemove(Path.GetFullPath(directory), out _);
    }

    /// <summary>Reads one document from disk. Overridable so tests can count reads.</summary>
    protected virtual CharacterAnnotationDocument? ReadDocument(string documentPath)
    {
        if (!File.Exists(documentPath))
            return null;

        try
        {
            using var stream = File.OpenRead(documentPath);
            var document = JsonSerializer.Deserialize<CharacterAnnotationDocument>(stream, JsonOptions);
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
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private IReadOnlyDictionary<string, CharacterAnnotationEntry> LoadDirectory(string directory)
        => _cache.GetOrAdd(
            Path.GetFullPath(directory),
            dir => new Lazy<IReadOnlyDictionary<string, CharacterAnnotationEntry>>(
                () => ReadEntries(dir),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private IReadOnlyDictionary<string, CharacterAnnotationEntry> ReadEntries(string directory)
    {
        var document = ReadDocument(GetDocumentPath(directory));
        if (document?.Cards is not { Count: > 0 } cards)
            return Empty;

        // JsonSerializer cannot be told a comparer, so the dictionary it
        // produces is case-sensitive; rebuild it to match how Windows compares
        // file names.
        return new Dictionary<string, CharacterAnnotationEntry>(cards, StringComparer.OrdinalIgnoreCase);
    }

    private static Lazy<IReadOnlyDictionary<string, CharacterAnnotationEntry>> Lazy(
        IReadOnlyDictionary<string, CharacterAnnotationEntry> value)
        => new(value);

    private static bool IsValid(CharacterAnnotationDocument document)
        => document.SchemaVersion is >= 1 and <= CharacterAnnotationDocument.CurrentSchemaVersion
           && document.Cards is not null;

    private static long SafeFileSize(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void DeleteDocument(string documentPath)
    {
        try
        {
            if (File.Exists(documentPath))
                File.Delete(documentPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leaving a stale file behind is better than failing the edit; the
            // next write replaces it.
        }
    }

    private static async Task WriteDocumentAtomicallyAsync(
        string path,
        CharacterAnnotationDocument document,
        CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        try
        {
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
        }
        finally
        {
            if (temporaryPath is not null)
                File.Delete(temporaryPath);
        }
    }

    private static void MarkHiddenOnWindows(string path)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Hidden) == 0)
            File.SetAttributes(path, attributes | FileAttributes.Hidden);
    }
}

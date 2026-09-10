using System.Collections.Concurrent;
using System.Text.Json;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Stores a local source's description next to its card folder. Like
/// <see cref="PostMetadataStore"/> this sits outside the app cache so it
/// travels with the library, but it is a separate store: that one records a
/// fetched artwork, keyed by provider and artwork id, while a local source has
/// neither.
/// </summary>
internal sealed class LocalSourceStore
{
    public const string DocumentFileName = "source.json";

    /// <summary>Stored avatar file name, without its extension.</summary>
    public const string AvatarBaseName = "avatar";

    /// <summary>
    /// Image formats an avatar may be stored as, lower case and with the dot.
    /// Deliberately short: these are what BitmapImage decodes without a codec
    /// the user might not have.
    /// </summary>
    public static readonly IReadOnlySet<string> AvatarExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteLocks =
        new(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    public string GetDocumentPath(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);

        return Path.Combine(
            Path.GetFullPath(sourceDirectory),
            PostMetadataStore.MetadataDirectoryName,
            DocumentFileName);
    }

    /// <summary>
    /// Absolute path of the source's avatar, or null when it has none. Absolute
    /// because AuthorDisplay turns the path straight into a Uri.
    /// </summary>
    public string? GetAvatarPath(string sourceDirectory, LocalSourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.AvatarFileName))
            return null;

        var fileName = Path.GetFileName(document.AvatarFileName);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var path = Path.GetFullPath(Path.Combine(
            Path.GetFullPath(sourceDirectory),
            PostMetadataStore.MetadataDirectoryName,
            fileName));

        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Copies <paramref name="imagePath"/> into the source's metadata
    /// directory and returns the file name to store, or null when the image
    /// could not be copied.
    /// </summary>
    /// <remarks>
    /// The file name is fixed apart from its extension, so replacing an avatar
    /// leaves no orphan behind — except when the extension changes, which is
    /// why the other candidates are deleted afterwards. The copy is what makes
    /// the avatar travel with the library: the chosen image may be anywhere,
    /// including a cache another machine will not have.
    /// </remarks>
    public async Task<string?> CopyAvatarAsync(
        string sourceDirectory,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        var extension = Path.GetExtension(imagePath);
        if (!AvatarExtensions.Contains(extension))
            return null;

        var metadataDirectory = Path.Combine(
            Path.GetFullPath(sourceDirectory),
            PostMetadataStore.MetadataDirectoryName);
        var fileName = AvatarBaseName + extension.ToLowerInvariant();
        var destination = Path.Combine(metadataDirectory, fileName);

        Directory.CreateDirectory(metadataDirectory);
        MarkHiddenOnWindows(metadataDirectory);

        var temporaryPath = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var input = File.OpenRead(imagePath))
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, destination, overwrite: true);
        }
        catch (Exception)
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }

        DeleteOtherAvatarFiles(metadataDirectory, fileName);
        return fileName;
    }

    /// <summary>Removes every stored avatar file for a source.</summary>
    public void DeleteAvatarFiles(string sourceDirectory)
        => DeleteOtherAvatarFiles(
            Path.Combine(
                Path.GetFullPath(sourceDirectory),
                PostMetadataStore.MetadataDirectoryName),
            keep: null);

    private static void DeleteOtherAvatarFiles(string metadataDirectory, string? keep)
    {
        foreach (var extension in AvatarExtensions)
        {
            var candidate = AvatarBaseName + extension;
            if (keep is not null && string.Equals(candidate, keep, StringComparison.OrdinalIgnoreCase))
                continue;

            var path = Path.Combine(metadataDirectory, candidate);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An avatar the user replaced is not worth failing the edit for.
            }
        }
    }

    /// <summary>
    /// Reads the source description, or null when it is absent, unreadable or
    /// malformed. A damaged document is never fatal: the folder name alone
    /// still carries the identity.
    /// </summary>
    public LocalSourceDocument? Read(string sourceDirectory)
        => ReadFile(GetDocumentPath(sourceDirectory));

    public async Task WriteAsync(
        string sourceDirectory,
        LocalSourceDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsValid(document))
            throw new ArgumentException("The local source document is invalid.", nameof(document));

        var path = GetDocumentPath(sourceDirectory);
        var writeLock = WriteLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var metadataDirectory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(metadataDirectory);
            MarkHiddenOnWindows(metadataDirectory);

            await WriteDocumentAtomicallyAsync(path, document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    /// Rewrites the parts a user can edit, preserving the identity, creation
    /// time and anything this version does not know about.
    /// </summary>
    /// <param name="clearAvatar">
    /// Removes the stored avatar. A separate flag because a null
    /// <paramref name="avatarFileName"/> means "leave it alone", which is what
    /// every caller that only renames the source wants.
    /// </param>
    public async Task<LocalSourceDocument> UpdateAsync(
        string sourceDirectory,
        string id,
        string displayName,
        IReadOnlyList<string>? aliases = null,
        string? note = null,
        string? avatarFileName = null,
        bool clearAvatar = false,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = Read(sourceDirectory);
        var document = new LocalSourceDocument(
            LocalSourceDocument.CurrentSchemaVersion,
            id,
            displayName)
        {
            Aliases = aliases ?? existing?.Aliases ?? [],
            Note = note ?? existing?.Note,
            AvatarFileName = clearAvatar ? null : avatarFileName ?? existing?.AvatarFileName,
            CreatedAt = existing?.CreatedAt is { } created && created != default ? created : now,
            UpdatedAt = now,
        };

        await WriteAsync(sourceDirectory, document, cancellationToken).ConfigureAwait(false);
        return document;
    }

    private static async Task WriteDocumentAtomicallyAsync(
        string path,
        LocalSourceDocument document,
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

    private static LocalSourceDocument? ReadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<LocalSourceDocument>(stream, JsonOptions);
            if (document is not null && document.Aliases is null)
                document = document with { Aliases = [] };
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

    private static bool IsValid(LocalSourceDocument document)
        => document.SchemaVersion is >= 1 and <= LocalSourceDocument.CurrentSchemaVersion
            && LocalSourceIdentity.IsValidId(document.Id)
            && !string.IsNullOrWhiteSpace(document.DisplayName)
            && document.Aliases is not null
            && document.Aliases.All(static alias => !string.IsNullOrWhiteSpace(alias));

    private static void MarkHiddenOnWindows(string path)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Hidden) == 0)
            File.SetAttributes(path, attributes | FileAttributes.Hidden);
    }
}

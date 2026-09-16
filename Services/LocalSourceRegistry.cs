using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// One local source: a friend, or the user's own private stash. The directory
/// is null while the source has been created in the UI but no import has put a
/// folder on disk for it yet.
/// </summary>
public sealed record LocalSourceEntry(
    string Id,
    string DisplayName,
    string? Directory = null,
    string? AvatarPath = null,
    string? Note = null);

/// <summary>
/// What a rename did on disk. <see cref="FoldersLeftBehind"/> is not a failure
/// of the rename itself: the source is renamed everywhere the app reads it, and
/// only the folder label is stale.
/// </summary>
public sealed record LocalSourceRenameResult(
    string DisplayName,
    int FoldersRenamed,
    IReadOnlyList<string> FoldersLeftBehind);

/// <summary>
/// Knows which local sources exist and where their folders are. This is the
/// only lookup the built-in local provider needs, which is what keeps that
/// provider free of network access and of I/O on hot scan paths.
/// </summary>
/// <remarks>
/// Scanning is deliberately bounded to the local scope folder under each
/// library root plus any explicitly adopted paths. A full library walk is
/// already expensive elsewhere (ImportLibraryIndexer) and would buy nothing
/// here: a local source is always identified by its own folder name.
/// </remarks>
public sealed class LocalSourceRegistry
{
    /// <summary>Directory levels below the local scope folder that may hold a source folder.</summary>
    /// <remarks>
    /// Destinations are laid out as scope/game/rating/author, so an author
    /// folder sits at most three levels down. One spare level absorbs a future
    /// layout change without silently losing sources.
    /// </remarks>
    private const int MaxScanDepth = 4;

    private readonly LocalSourceStore _store = new();
    private readonly IAppLogger _logger;
    private readonly object _gate = new();

    private string[] _roots = [];
    private string[] _adoptedPaths = [];
    private string _importSubfolder = "";
    private string _localFolderName = "";
    private Dictionary<string, LocalSourceEntry> _sources = new(StringComparer.OrdinalIgnoreCase);

    // A source normally owns one folder per library root, because a card lands
    // under the root for its own type. LocalSourceEntry carries one directory
    // for display; the rest are kept here so a rename reaches all of them.
    private Dictionary<string, List<string>> _directoriesById = new(StringComparer.OrdinalIgnoreCase);

    public LocalSourceRegistry(IAppLogger logger) => _logger = logger;

    /// <summary>Raised after a rescan changed the known set of sources.</summary>
    public event Action? SourcesChanged;

    /// <summary>Folder name the local provider claims below the import subfolder.</summary>
    public string LocalFolderName
    {
        get { lock (_gate) return _localFolderName; }
    }

    public IReadOnlyList<LocalSourceEntry> Sources
    {
        get { lock (_gate) return [.. _sources.Values.OrderBy(s => s.DisplayName, StringComparer.CurrentCulture)]; }
    }

    /// <summary>Every folder a source owns, across the library roots.</summary>
    public IReadOnlyList<string> DirectoriesOf(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return [];

        lock (_gate)
        {
            if (_directoriesById.TryGetValue(id, out var known) && known.Count > 0)
                return [.. known];

            return _sources.TryGetValue(id, out var entry) && entry.Directory is { } single
                ? [single]
                : [];
        }
    }

    public LocalSourceEntry? Find(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        lock (_gate)
            return _sources.GetValueOrDefault(id);
    }

    /// <summary>
    /// Applies the current library configuration. Cheap and synchronous; call
    /// <see cref="RescanAsync"/> to pick up what is actually on disk.
    /// </summary>
    public void UpdateConfiguration(
        IEnumerable<string> roots,
        string importSubfolder,
        string localFolderName,
        IEnumerable<string>? adoptedPaths = null)
    {
        lock (_gate)
        {
            _roots = NormalizeDirectories(roots);
            _adoptedPaths = NormalizeDirectories(adoptedPaths ?? []);
            _importSubfolder = importSubfolder ?? "";
            _localFolderName = localFolderName ?? "";
        }
    }

    /// <summary>
    /// Registers a source the user just named, before any folder exists for it.
    /// The import that follows creates the folder; <see cref="EnsureDocumentAsync"/>
    /// then writes the description into it.
    /// </summary>
    public LocalSourceEntry CreatePending(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var entry = new LocalSourceEntry(LocalSourceIdentity.NewId(), displayName.Trim());
        lock (_gate)
            _sources[entry.Id] = entry;

        SourcesChanged?.Invoke();
        return entry;
    }

    /// <summary>
    /// Writes or refreshes the source description in a folder that now exists,
    /// and binds the source to that directory.
    /// </summary>
    public async Task EnsureDocumentAsync(
        string sourceDirectory,
        string id,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (!LocalSourceIdentity.IsValidId(id) || string.IsNullOrWhiteSpace(sourceDirectory))
            return;

        try
        {
            var document = await _store
                .UpdateAsync(sourceDirectory, id, displayName, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            Remember(Path.GetFullPath(sourceDirectory), document);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cards are already in place and the folder name still carries
            // the identity, so a failed description write is not fatal.
            _logger.LogError("LocalSource.EnsureDocument", ex, sourceDirectory);
        }
    }

    /// <summary>
    /// Re-reads one source's description from disk. Falls back to a rescan only
    /// when the id is unknown, so refreshing every author in turn does not
    /// trigger one full scan per author.
    /// </summary>
    public async Task<LocalSourceEntry?> ReloadAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (Find(id) is not { Directory: { } directory })
        {
            await RescanAsync(cancellationToken).ConfigureAwait(false);
            return Find(id);
        }

        var document = _store.Read(directory);
        if (document is not null && string.Equals(document.Id, id, StringComparison.OrdinalIgnoreCase))
            Remember(directory, document);

        return Find(id);
    }

    /// <summary>
    /// Renames a source, and with it the folders it owns.
    /// </summary>
    /// <param name="renameFolders">
    /// Whether the folders on disk are renamed too. The identity lives in the
    /// trailing id of a folder name, never in its label, so leaving the label
    /// stale costs nothing but a confusing file manager — which is exactly why
    /// this is the caller decision.
    /// </param>
    /// <remarks>
    /// The folder move comes before the description write, so the description
    /// is written where the cards now are. A folder that refuses to move is
    /// reported rather than retried: it means something holds a file open, and
    /// the source is still correctly named everywhere the app reads it.
    ///
    /// Renaming a folder changes every card path under it. The card services
    /// learn about that from their watchers, which expand a directory rename
    /// into per-card changes; path-keyed caches (thumbnails, metadata) miss and
    /// rebuild for those cards.
    /// </remarks>
    public async Task<LocalSourceRenameResult> RenameAsync(
        string id,
        string displayName,
        bool renameFolders = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var name = displayName.Trim();
        var existing = Find(id);
        if (existing?.Directory is null)
        {
            if (existing is not null)
            {
                lock (_gate)
                    _sources[id] = existing with { DisplayName = name };
                SourcesChanged?.Invoke();
            }

            return new LocalSourceRenameResult(name, 0, []);
        }

        var resulting = new List<string>();
        var leftBehind = new List<string>();
        var renamed = 0;

        // Every folder this source owns, so the name does not depend on which
        // library root happens to be scanned first next time.
        foreach (var directory in DirectoriesOf(id))
        {
            var target = renameFolders ? TargetDirectory(directory, name) : null;
            var landed = directory;
            if (target is not null)
            {
                try
                {
                    Directory.Move(directory, target);
                    landed = target;
                    renamed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogError("LocalSource.RenameFolder", ex, directory);
                    leftBehind.Add(directory);
                }
            }

            resulting.Add(landed);
            await EnsureDocumentAsync(landed, id, name, cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
            _directoriesById[id] = resulting;

        SourcesChanged?.Invoke();
        return new LocalSourceRenameResult(name, renamed, leftBehind);
    }

    /// <summary>
    /// Stores <paramref name="imagePath"/> as the avatar of a source, in every
    /// folder it owns so the picture survives whichever folder is resolved
    /// first, and returns the source with its new avatar.
    /// </summary>
    public async Task<LocalSourceEntry?> SetAvatarAsync(
        string id,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var existing = Find(id);
        if (existing is null)
            return null;

        foreach (var directory in DirectoriesOf(id))
        {
            try
            {
                var fileName = await _store
                    .CopyAvatarAsync(directory, imagePath, cancellationToken)
                    .ConfigureAwait(false);
                if (fileName is null)
                    continue;

                await _store
                    .UpdateAsync(
                        directory,
                        id,
                        existing.DisplayName,
                        avatarFileName: fileName,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError("LocalSource.SetAvatar", ex, directory);
            }
        }

        return await ReloadAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes the avatar, falling back to the initials tile.</summary>
    public async Task<LocalSourceEntry?> ClearAvatarAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var existing = Find(id);
        if (existing is null)
            return null;

        foreach (var directory in DirectoriesOf(id))
        {
            try
            {
                _store.DeleteAvatarFiles(directory);
                await _store
                    .UpdateAsync(
                        directory,
                        id,
                        existing.DisplayName,
                        clearAvatar: true,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError("LocalSource.ClearAvatar", ex, directory);
            }
        }

        return await ReloadAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Where a source folder moves for a new display name, or null when it
    /// should stay put.
    /// </summary>
    private static string? TargetDirectory(string directory, string displayName)
    {
        var parent = Path.GetDirectoryName(directory);
        var folderName = LocalSourceIdentity.RenameFolderName(Path.GetFileName(directory), displayName);
        if (parent is null || folderName is null)
            return null;

        var target = Path.Combine(parent, folderName);
        return Directory.Exists(target) ? null : target;
    }

    /// <summary>
    /// Starts a rescan without waiting for it. Until it lands, a local card
    /// still shows the name encoded in its folder; the description only
    /// refines it.
    /// </summary>
    public void BeginRescan()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await RescanAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError("LocalSource.Rescan", ex);
            }
        });
    }

    /// <summary>
    /// Finds every local source folder under the configured scopes. Pending
    /// sources with no folder yet are preserved.
    /// </summary>
    public Task RescanAsync(CancellationToken cancellationToken = default)
    {
        string[] roots;
        string[] adoptedPaths;
        string scopeRelativePath;

        lock (_gate)
        {
            roots = _roots;
            adoptedPaths = _adoptedPaths;
            scopeRelativePath = PathSanitizer.SanitizeRelativePath(
                Path.Combine(_importSubfolder, _localFolderName));
        }

        return Task.Run(() => Rescan(roots, adoptedPaths, scopeRelativePath, cancellationToken), cancellationToken);
    }

    private void Rescan(
        string[] roots,
        string[] adoptedPaths,
        string scopeRelativePath,
        CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, LocalSourceEntry>(StringComparer.OrdinalIgnoreCase);
        var directories = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in EnumerateCandidateDirectories(roots, adoptedPaths, scopeRelativePath, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!LocalSourceIdentity.TryParseFolderId(
                    Path.GetFileName(directory), out var id, out var folderName))
            {
                continue;
            }

            var full = Path.GetFullPath(directory);
            if (!directories.TryGetValue(id, out var known))
                directories[id] = known = [];
            if (!known.Contains(full, StringComparer.OrdinalIgnoreCase))
                known.Add(full);

            // Several folders for one id is the normal case, not a conflict:
            // scene cards land under the scene root and character cards under
            // the character root. The first one found describes the source; the
            // others are remembered above so a rename can reach them too.
            var entry = BuildEntry(directory, id, folderName);
            if (!found.TryGetValue(id, out var existing))
            {
                found[id] = entry;
            }
            else if (existing.AvatarPath is null && entry.AvatarPath is not null)
            {
                // Prefer whichever folder actually carries a description.
                found[id] = entry with { Directory = existing.Directory };
            }
        }

        var changed = false;
        lock (_gate)
        {
            foreach (var pending in _sources.Values.Where(s => s.Directory is null))
                found.TryAdd(pending.Id, pending);

            _directoriesById = directories;
            if (!SameSources(_sources, found))
            {
                _sources = found;
                changed = true;
            }
        }

        if (changed)
            SourcesChanged?.Invoke();
    }

    private IEnumerable<string> EnumerateCandidateDirectories(
        string[] roots,
        string[] adoptedPaths,
        string scopeRelativePath,
        CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = MaxScanDepth,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scope = string.IsNullOrEmpty(scopeRelativePath)
                ? root
                : Path.Combine(root, scopeRelativePath);
            if (!Directory.Exists(scope))
                continue;

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(scope, "*", options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError("LocalSource.Scan", ex, scope);
                continue;
            }

            foreach (var directory in directories)
                yield return directory;
        }

        // Adopted folders are named by their owner, so they are candidates in
        // their own right rather than something to search below.
        foreach (var path in adoptedPaths)
        {
            if (Directory.Exists(path))
                yield return path;
        }
    }

    private LocalSourceEntry BuildEntry(string directory, string id, string folderName)
    {
        var document = _store.Read(directory);
        if (document is null || !string.Equals(document.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            // No description, or one whose identity disagrees with the folder.
            // The folder name is authoritative for identity, so fall back to it.
            return new LocalSourceEntry(
                id,
                string.IsNullOrEmpty(folderName) ? id : folderName,
                Path.GetFullPath(directory));
        }

        return ToEntry(Path.GetFullPath(directory), document);
    }

    private void Remember(string directory, LocalSourceDocument document)
    {
        lock (_gate)
            _sources[document.Id] = ToEntry(directory, document);

        SourcesChanged?.Invoke();
    }

    private LocalSourceEntry ToEntry(string directory, LocalSourceDocument document)
        => new(
            document.Id,
            document.DisplayName,
            directory,
            _store.GetAvatarPath(directory, document),
            document.Note);

    private static bool SameSources(
        Dictionary<string, LocalSourceEntry> current,
        Dictionary<string, LocalSourceEntry> candidate)
        => current.Count == candidate.Count
            && current.All(pair =>
                candidate.TryGetValue(pair.Key, out var other) && pair.Value == other);

    private static string[] NormalizeDirectories(IEnumerable<string> directories)
        => [.. directories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Select(path => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}

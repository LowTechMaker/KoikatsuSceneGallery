using System.Collections.ObjectModel;
using System.Collections.Specialized;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using Microsoft.UI.Dispatching;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

public enum AuthorCardKind
{
    Scene,
    Character,
    Coordinate,
}

public sealed record AuthorSummary(
    AuthorDisplay Display,
    int SceneCount,
    int CharacterCount,
    int CoordinateCount,
    DateTime LastUpdated)
{
    public IReadOnlyList<string> ThumbnailPaths { get; init; } = [];
    public int TotalCount => SceneCount + CharacterCount + CoordinateCount;
}

public sealed record AuthorProviderInfo(string ProviderId, string DisplayName);

/// <summary>
/// Bridges loaded <see cref="IFolderAuthorProvider"/> plugins and the
/// galleries: resolves each card's folder chain to an author, assigns the
/// shared <see cref="AuthorDisplay"/> to cards as they enter the collections,
/// and kicks off (plugin-rate-limited) profile fetches. When no plugin is
/// installed every entry point no-ops, so the rest of the app is unaffected.
///
/// All state is touched on the UI thread only: cards are added/removed inside
/// dispatcher work items, and fetch completions are marshalled back here.
/// </summary>
public sealed class AuthorInfoService
{
    private readonly IReadOnlyList<IFolderAuthorProvider> _providers;
    private readonly DispatcherQueue _dispatcher;
    private readonly IAppLogger _logger;

    // Folder-chain resolution memoized per directory; cleared when the
    // configured roots change (which also reloads the galleries).
    private readonly Dictionary<string, AuthorDisplay?> _directoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AuthorKey, AuthorDisplay> _displays = [];
    private readonly Dictionary<AuthorCardKind, Dictionary<AuthorDisplay, int>> _counts = new()
    {
        [AuthorCardKind.Scene] = [],
        [AuthorCardKind.Character] = [],
        [AuthorCardKind.Coordinate] = [],
    };
    private readonly Dictionary<AuthorCardKind, Dictionary<AuthorDisplay, List<DateTime>>> _updatedTimes = new()
    {
        [AuthorCardKind.Scene] = [],
        [AuthorCardKind.Character] = [],
        [AuthorCardKind.Coordinate] = [],
    };
    private List<string> _roots = [];
    private bool _isRebuilding;

    /// <summary>Fired (on the UI thread) whenever author assignments or counts change.</summary>
    public event Action? AuthorsChanged;

    public AuthorInfoService(IReadOnlyList<IFolderAuthorProvider> providers, DispatcherQueue dispatcher, IAppLogger logger)
    {
        _providers = providers
            .GroupBy(p => p.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        ProviderInfos = _providers
            .Select(p => new AuthorProviderInfo(p.ProviderId, GetDisplayName(p)))
            .ToList();
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public bool IsAvailable => _providers.Count > 0;

    public IReadOnlyList<AuthorProviderInfo> ProviderInfos { get; }

    public void UpdateRoots(IEnumerable<string> roots)
    {
        if (_providers.Count == 0) return;
        _roots = roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _directoryCache.Clear();
    }

    /// <summary>
    /// Starts tracking a gallery's card collection. Handlers run on the UI
    /// thread (the galleries only mutate Cards from dispatcher work), and the
    /// per-card cost is a memoized dictionary lookup, so this adds no
    /// measurable weight to the scan path.
    /// </summary>
    public void Attach<TCard>(ObservableCollection<TCard> cards, AuthorCardKind kind)
        where TCard : class, IAuthorOwner
    {
        if (_providers.Count == 0) return;

        cards.CollectionChanged += (_, e) =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (TCard card in e.NewItems!)
                        AssignAuthor(card, kind);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    foreach (TCard card in e.OldItems!)
                        UnassignAuthor(card, kind);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    foreach (TCard card in e.OldItems!)
                        UnassignAuthor(card, kind);
                    foreach (TCard card in e.NewItems!)
                        AssignAuthor(card, kind);
                    break;
                case NotifyCollectionChangedAction.Reset:
                    _counts[kind].Clear();
                    _updatedTimes[kind].Clear();
                    PruneOrphanedDisplays();
                    AuthorsChanged?.Invoke();
                    break;
            }
        };
    }

    /// <summary>Snapshot of all known authors with per-gallery card counts.</summary>
    public List<AuthorSummary> GetSummaries()
    {
        var summaries = new List<AuthorSummary>(_displays.Count);
        foreach (var display in _displays.Values)
        {
            summaries.Add(new AuthorSummary(
                display,
                _counts[AuthorCardKind.Scene].GetValueOrDefault(display),
                _counts[AuthorCardKind.Character].GetValueOrDefault(display),
                _counts[AuthorCardKind.Coordinate].GetValueOrDefault(display),
                GetLastUpdated(display)));
        }
        return summaries;
    }

    public void RebuildAssignments(
        IEnumerable<SceneCard> scenes,
        IEnumerable<CharacterCard> characters,
        IEnumerable<CoordinateCard> coordinates)
    {
        if (_providers.Count == 0) return;

        _isRebuilding = true;
        try
        {
            _directoryCache.Clear();
            _displays.Clear();
            foreach (var counts in _counts.Values)
                counts.Clear();
            foreach (var updatedTimes in _updatedTimes.Values)
                updatedTimes.Clear();

            foreach (var card in scenes)
            {
                card.Author = null;
                AssignAuthor(card, AuthorCardKind.Scene);
            }

            foreach (var card in characters)
            {
                card.Author = null;
                AssignAuthor(card, AuthorCardKind.Character);
            }

            foreach (var card in coordinates)
            {
                card.Author = null;
                AssignAuthor(card, AuthorCardKind.Coordinate);
            }
        }
        finally
        {
            _isRebuilding = false;
        }

        AuthorsChanged?.Invoke();
    }

    /// <summary>Re-fetches one author from the network, bypassing the plugin's cache.</summary>
    public async Task RefreshAuthorAsync(AuthorKey key, CancellationToken ct = default)
    {
        var provider = FindProvider(key.ProviderId);
        if (provider is null) return;
        var info = await provider.GetAuthorInfoAsync(key, forceRefresh: true, ct);
        if (info is not null)
            ApplyInfo(info);
    }

    private void AssignAuthor(IAuthorOwner card, AuthorCardKind kind)
    {
        var directory = Path.GetDirectoryName(card.FilePath);
        if (directory is null) return;

        var display = ResolveDirectory(directory);
        if (display is null) return;

        card.Author = display;
        var counts = _counts[kind];
        counts[display] = counts.GetValueOrDefault(display) + 1;
        AddUpdatedTime(kind, display, card.DateModified);
        NotifyAuthorsChanged();
    }

    private void UnassignAuthor(IAuthorOwner card, AuthorCardKind kind)
    {
        if (card.Author is not { } display) return;
        var counts = _counts[kind];
        if (counts.TryGetValue(display, out var count))
        {
            if (count <= 1) counts.Remove(display);
            else counts[display] = count - 1;
        }
        RemoveUpdatedTime(kind, display, card.DateModified);
        NotifyAuthorsChanged();
    }

    private void PruneOrphanedDisplays()
    {
        List<AuthorKey>? toRemove = null;
        foreach (var (key, display) in _displays)
        {
            bool hasCount = false;
            foreach (var kindCounts in _counts.Values)
            {
                if (kindCounts.ContainsKey(display)) { hasCount = true; break; }
            }
            if (!hasCount)
                (toRemove ??= []).Add(key);
        }
        if (toRemove is null) return;
        foreach (var key in toRemove)
        {
            if (_displays.TryGetValue(key, out var display))
            {
                foreach (var updatedTimes in _updatedTimes.Values)
                    updatedTimes.Remove(display);
            }
            _displays.Remove(key);
        }
    }

    private void AddUpdatedTime(AuthorCardKind kind, AuthorDisplay display, DateTime updated)
    {
        var updatedTimes = _updatedTimes[kind];
        if (!updatedTimes.TryGetValue(display, out var times))
            updatedTimes[display] = times = [];
        times.Add(updated);
    }

    private void RemoveUpdatedTime(AuthorCardKind kind, AuthorDisplay display, DateTime updated)
    {
        var updatedTimes = _updatedTimes[kind];
        if (!updatedTimes.TryGetValue(display, out var times))
            return;

        times.Remove(updated);
        if (times.Count == 0)
            updatedTimes.Remove(display);
    }

    private DateTime GetLastUpdated(AuthorDisplay display)
    {
        var latest = DateTime.MinValue;
        foreach (var updatedTimes in _updatedTimes.Values)
        {
            if (updatedTimes.TryGetValue(display, out var times) && times.Count > 0)
                latest = latest > times.Max() ? latest : times.Max();
        }
        return latest;
    }

    /// <summary>
    /// Resolves a directory to an author by walking up from the file's
    /// directory toward the library root and returning the highest
    /// (farthest-from-file) match. This avoids treating artwork subfolders
    /// like "Title (artworkId)" as authors when the real author folder sits
    /// one level above.
    /// </summary>
    private AuthorDisplay? ResolveDirectory(string directory)
    {
        if (_directoryCache.TryGetValue(directory, out var cached))
            return cached;

        AuthorDisplay? result = null;
        if (IsAtOrBelowRoot(directory))
        {
            // Check ancestors first so the outermost match wins.
            if (Path.GetDirectoryName(directory) is { } parent)
                result = ResolveDirectory(parent);

            // Only parse this folder if no ancestor matched.
            if (result is null)
            {
                var folderName = Path.GetFileName(directory);
                foreach (var provider in GetCandidateProviders(directory))
                {
                    var parsed = provider.TryParseFolderName(folderName);
                    if (parsed is null) continue;

                    result = GetOrCreateDisplay(provider, parsed);
                    break;
                }
            }
        }

        _directoryCache[directory] = result;
        return result;
    }

    private bool IsAtOrBelowRoot(string directory)
    {
        foreach (var root in _roots)
        {
            if (directory.Length == root.Length &&
                directory.Equals(root, StringComparison.OrdinalIgnoreCase))
                return true;
            if (directory.Length > root.Length &&
                directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                (directory[root.Length] == Path.DirectorySeparatorChar ||
                 directory[root.Length] == Path.AltDirectorySeparatorChar))
                return true;
        }
        return false;
    }

    private AuthorDisplay GetOrCreateDisplay(IFolderAuthorProvider provider, ParsedAuthor parsed)
    {
        if (_displays.TryGetValue(parsed.Key, out var existing))
            return existing;

        var display = new AuthorDisplay(parsed.Key, parsed.FolderDisplayName, provider.GetProfileUrl(parsed.Key));
        _displays[parsed.Key] = display;

        // Fire-and-forget: cache hits complete synchronously inside the
        // plugin; misses queue behind its rate limiter. Either way the result
        // lands back on the UI thread as plain property updates.
        FetchAsync(parsed.Key).Observe(_logger, "Author.Fetch");
        return display;
    }

    private async Task FetchAsync(AuthorKey key)
    {
        try
        {
            var provider = FindProvider(key.ProviderId);
            if (provider is null) return;

            var info = await provider.GetAuthorInfoAsync(key, forceRefresh: false, CancellationToken.None);
            if (info is not null)
                ApplyInfo(info);
        }
        catch (Exception ex)
        {
            // A failed fetch just leaves the folder-derived name in place.
            _logger.LogError("Author.Fetch", ex, key.Id);
        }
    }

    private void ApplyInfo(AuthorInfo info)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (!_displays.TryGetValue(info.Key, out var display)) return;
            display.Name = info.Name;
            display.AvatarPath = info.AvatarFilePath;
            NotifyAuthorsChanged();
        });
    }

    private void NotifyAuthorsChanged()
    {
        if (!_isRebuilding)
            AuthorsChanged?.Invoke();
    }

    private IFolderAuthorProvider? FindProvider(string providerId)
        => _providers.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<IFolderAuthorProvider> GetCandidateProviders(string directory)
    {
        var scoped = _providers
            .Where(p => IsInsideProviderScope(directory, p))
            .ToList();
        return scoped.Count > 0 ? scoped : _providers;
    }

    private bool IsInsideProviderScope(string directory, IFolderAuthorProvider provider)
    {
        if (provider is not IImportDestinationProvider destinationProvider)
            return false;

        var providerFolder = PathSanitizer.SanitizeRelativePath(destinationProvider.DestinationFolderName);
        if (string.IsNullOrEmpty(providerFolder))
            return false;

        var providerParts = SplitPath(providerFolder);
        if (providerParts.Length == 0)
            return false;

        foreach (var root in _roots)
        {
            if (!TryGetRelativePath(directory, root, out var relativePath))
                continue;

            var directoryParts = SplitPath(relativePath);
            for (var i = 0; i <= directoryParts.Length - providerParts.Length; i++)
            {
                var matches = true;
                for (var j = 0; j < providerParts.Length; j++)
                {
                    if (!directoryParts[i + j].Equals(providerParts[j], StringComparison.OrdinalIgnoreCase))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetRelativePath(string directory, string root, out string relativePath)
    {
        relativePath = string.Empty;
        if (directory.Length == root.Length &&
            directory.Equals(root, StringComparison.OrdinalIgnoreCase))
            return true;

        if (directory.Length <= root.Length ||
            !directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            (directory[root.Length] != Path.DirectorySeparatorChar &&
             directory[root.Length] != Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        relativePath = directory[(root.Length + 1)..];
        return true;
    }

    private static string[] SplitPath(string path)
        => path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

    private static string GetDisplayName(IFolderAuthorProvider provider)
    {
        if (provider is IImportDestinationProvider destinationProvider)
        {
            var providerFolder = PathSanitizer.SanitizeRelativePath(destinationProvider.DestinationFolderName);
            if (!string.IsNullOrEmpty(providerFolder))
                return Path.GetFileName(providerFolder);
        }

        return string.IsNullOrWhiteSpace(provider.Name) ? provider.ProviderId : provider.Name;
    }
}

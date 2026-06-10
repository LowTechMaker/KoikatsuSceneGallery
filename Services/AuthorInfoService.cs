using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    int CoordinateCount)
{
    public int TotalCount => SceneCount + CharacterCount + CoordinateCount;
}

/// <summary>
/// Bridges the loaded <see cref="IFolderAuthorProvider"/> plugin and the
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
    private readonly IFolderAuthorProvider? _provider;
    private readonly DispatcherQueue _dispatcher;

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
    private List<string> _roots = [];

    /// <summary>Fired (on the UI thread) whenever author assignments or counts change.</summary>
    public event Action? AuthorsChanged;

    public AuthorInfoService(IFolderAuthorProvider? provider, DispatcherQueue dispatcher)
    {
        _provider = provider;
        _dispatcher = dispatcher;
    }

    public bool IsAvailable => _provider != null;

    public void UpdateRoots(IEnumerable<string> roots)
    {
        if (_provider is null) return;
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
        if (_provider is null) return;

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
                case NotifyCollectionChangedAction.Reset:
                    _counts[kind].Clear();
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
                _counts[AuthorCardKind.Coordinate].GetValueOrDefault(display)));
        }
        return summaries;
    }

    /// <summary>Re-fetches one author from the network, bypassing the plugin's cache.</summary>
    public async Task RefreshAuthorAsync(AuthorKey key, CancellationToken ct = default)
    {
        if (_provider is null) return;
        var info = await _provider.GetAuthorInfoAsync(key, forceRefresh: true, ct);
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
        AuthorsChanged?.Invoke();
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
        AuthorsChanged?.Invoke();
    }

    /// <summary>
    /// Resolves a directory to an author by parsing its name and, failing
    /// that, its ancestors' names — but never above a configured library root,
    /// so unrelated path segments (e.g. a user folder with digits) can't
    /// produce phantom authors. Nearest-to-file match wins.
    /// </summary>
    private AuthorDisplay? ResolveDirectory(string directory)
    {
        if (_directoryCache.TryGetValue(directory, out var cached))
            return cached;

        AuthorDisplay? result = null;
        if (IsAtOrBelowRoot(directory))
        {
            var parsed = _provider!.TryParseFolderName(Path.GetFileName(directory));
            result = parsed is not null
                ? GetOrCreateDisplay(parsed)
                : Path.GetDirectoryName(directory) is { } parent ? ResolveDirectory(parent) : null;
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

    private AuthorDisplay GetOrCreateDisplay(ParsedAuthor parsed)
    {
        if (_displays.TryGetValue(parsed.Key, out var existing))
            return existing;

        var display = new AuthorDisplay(parsed.Key, parsed.FolderDisplayName, _provider!.GetProfileUrl(parsed.Key));
        _displays[parsed.Key] = display;

        // Fire-and-forget: cache hits complete synchronously inside the
        // plugin; misses queue behind its rate limiter. Either way the result
        // lands back on the UI thread as plain property updates.
        _ = FetchAsync(parsed.Key);
        return display;
    }

    private async Task FetchAsync(AuthorKey key)
    {
        try
        {
            var info = await _provider!.GetAuthorInfoAsync(key, forceRefresh: false, CancellationToken.None);
            if (info is not null)
                ApplyInfo(info);
        }
        catch (Exception ex)
        {
            // A failed fetch just leaves the folder-derived name in place.
            Helpers.CrashLog.Write("AuthorFetch", ex);
        }
    }

    private void ApplyInfo(AuthorInfo info)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (!_displays.TryGetValue(info.Key, out var display)) return;
            display.Name = info.Name;
            display.AvatarPath = info.AvatarFilePath;
            AuthorsChanged?.Invoke();
        });
    }
}

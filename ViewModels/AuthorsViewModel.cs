using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.ApplicationModel.Resources;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

public enum AuthorSortMode
{
    Count,
    LastUpdated,
    Name,
}

public partial class AuthorGroupViewModel(string key, string header, bool showHeader = true) : ObservableObject
{
    public string Key { get; } = key;

    public string Header { get; } = header;

    [ObservableProperty]
    public partial bool ShowHeader { get; set; } = showHeader;

    public ObservableCollection<AuthorSummary> Authors { get; } = [];

    public int AuthorCount => Authors.Count;

    public string CountText => AuthorCount > 0 ? $"({AuthorCount})" : "";

    public void NotifyCountChanged()
    {
        OnPropertyChanged(nameof(AuthorCount));
        OnPropertyChanged(nameof(CountText));
    }
}

public partial class AuthorIndexItemViewModel(string key, string display, AuthorGroupViewModel? group) : ObservableObject
{
    public string Key { get; } = key;

    public string Display { get; } = display;

    public AuthorGroupViewModel? Group { get; } = group;

    public bool IsAvailable => Group is not null;

    public double Opacity => IsAvailable ? 1.0 : 0.35;
}

public partial class AuthorProviderTabViewModel : ObservableObject
{
    public AuthorProviderTabViewModel(AuthorProviderInfo provider)
    {
        ProviderId = provider.ProviderId;
        DisplayName = provider.DisplayName;
    }

    public string ProviderId { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    public partial IReadOnlyList<AuthorSummary> Authors { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<AuthorGroupViewModel> Groups { get; set; } = [];

    public ObservableCollection<AuthorIndexItemViewModel> QuickJumpItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasAuthors { get; set; }

    public bool IsEmpty => !HasAuthors;

    [ObservableProperty]
    public partial int AuthorCount { get; set; }

    public string CountText => AuthorCount > 0 ? $"({AuthorCount})" : "";

    partial void OnAuthorCountChanged(int value) => OnPropertyChanged(nameof(CountText));
}

/// <summary>
/// Backs the Authors page: an aggregated, count-sorted list of every author
/// detected across the three galleries. Rebuilds are debounced because
/// AuthorsChanged fires per card during a bulk gallery load.
/// </summary>
public partial class AuthorsViewModel : ObservableObject
{
    private static readonly TimeSpan InitialRebuildDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan RebuildDebounce = TimeSpan.FromMilliseconds(500);
    private static readonly ResourceLoader ResLoader = new();

    private readonly AuthorInfoService _authorInfoService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly ThumbnailCacheService _thumbnailCacheService;
    private readonly GalleryViewModel _galleryViewModel;
    private readonly IAppLogger _logger;
    private readonly DispatcherQueueTimer _rebuildTimer;
    private Dictionary<AuthorDisplay, IReadOnlyList<string>>? _thumbnailCache;
    private CancellationTokenSource? _rebuildCts;
    private int _rebuildGeneration;
    private bool _isActive;
    private bool _rebuildPending = true;

    public ObservableCollection<AuthorProviderTabViewModel> ProviderTabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowInitialLoading))]
    public partial bool HasAuthors { get; set; }

    public bool IsEmpty => !HasAuthors;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInitialLoading))]
    public partial bool IsLoading { get; set; }

    public bool ShowInitialLoading => IsLoading && !HasAuthors;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => QueueRebuild();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSortByName))]
    [NotifyPropertyChangedFor(nameof(SortModeIndex))]
    public partial AuthorSortMode SortMode { get; set; }

    public bool IsSortByName => SortMode == AuthorSortMode.Name;

    public int SortModeIndex => (int)SortMode;

    partial void OnSortModeChanged(AuthorSortMode value) => QueueRebuild();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRefreshing))]
    public partial bool IsRefreshing { get; set; }

    public bool IsNotRefreshing => !IsRefreshing;

    /// <summary>Numeric "done / total" progress while a refresh-all runs.</summary>
    [ObservableProperty]
    public partial string RefreshProgress { get; set; } = string.Empty;

    public AuthorsViewModel(
        AuthorInfoService authorInfoService,
        DispatcherQueue dispatcher,
        SettingsViewModel settingsViewModel,
        ThumbnailCacheService thumbnailCacheService,
        GalleryViewModel galleryViewModel,
        IAppLogger logger)
    {
        _authorInfoService = authorInfoService;
        _settingsViewModel = settingsViewModel;
        _thumbnailCacheService = thumbnailCacheService;
        _galleryViewModel = galleryViewModel;
        _logger = logger;
        foreach (var provider in _authorInfoService.ProviderInfos)
            ProviderTabs.Add(new AuthorProviderTabViewModel(provider));

        _rebuildTimer = dispatcher.CreateTimer();
        _rebuildTimer.Interval = RebuildDebounce;
        _rebuildTimer.IsRepeating = false;
        _rebuildTimer.Tick += (_, _) => _ = RebuildAsync();

        _authorInfoService.AuthorsChanged += OnAuthorsChanged;
        _authorInfoService.AuthorProfilesChanged += OnAuthorProfilesChanged;
    }

    public void Activate()
    {
        _isActive = true;
        if (_rebuildPending)
        {
            IsLoading = true;
            RestartRebuildTimer(InitialRebuildDelay);
        }
    }

    public void Deactivate()
    {
        _isActive = false;
        _rebuildTimer.Stop();
        if (_rebuildCts is not null)
        {
            _rebuildPending = true;
            _rebuildGeneration++;
            _rebuildCts.Cancel();
        }
        IsLoading = false;
    }

    private void OnAuthorsChanged()
    {
        _thumbnailCache = null;
        QueueRebuild();
    }

    private void OnAuthorProfilesChanged(AuthorKey _)
    {
        // Tiles observe AuthorDisplay directly. A collection rebuild is only
        // needed when the profile name affects filtering or primary ordering.
        if (SortMode == AuthorSortMode.Name || !string.IsNullOrWhiteSpace(SearchText))
            QueueRebuild();
    }

    private void QueueRebuild()
    {
        _rebuildPending = true;
        _rebuildGeneration++;
        _rebuildCts?.Cancel();
        if (!_isActive)
            return;

        // Restarting makes this a trailing debounce, so a burst of profile or
        // assignment changes produces one rebuild after the burst settles.
        IsLoading = true;
        RestartRebuildTimer(RebuildDebounce);
    }

    private void RestartRebuildTimer(TimeSpan interval)
    {
        _rebuildTimer.Stop();
        _rebuildTimer.Interval = interval;
        _rebuildTimer.Start();
    }

    private async Task RebuildAsync()
    {
        _rebuildTimer.Stop();
        if (!_isActive)
        {
            _rebuildPending = true;
            IsLoading = false;
            return;
        }

        var generation = _rebuildGeneration;
        _rebuildPending = false;
        var cts = new CancellationTokenSource();
        var previousCts = _rebuildCts;
        _rebuildCts = cts;
        previousCts?.Cancel();
        IsLoading = true;

        try
        {
            var search = SearchText.Trim();
            var sortMode = SortMode;
            var liveTilesEnabled = _settingsViewModel.AuthorLiveTilesEnabled;
            var sourceSnapshots = _authorInfoService.GetSummaries()
                .Select(summary => new AuthorSortSnapshot(
                    summary,
                    summary.Display.Name,
                    summary.Display.Key.Id,
                    summary.Display.ProfileUrl))
                .ToList();
            var thumbnailCandidates = liveTilesEnabled && _thumbnailCache is null
                ? CaptureThumbnailCandidates()
                : null;
            var cachedThumbnails = _thumbnailCache;

            var summaries = await Task.Run(
                () => PrepareSummaries(
                    sourceSnapshots,
                    search,
                    sortMode,
                    cts.Token));

            if (cts.IsCancellationRequested ||
                !_isActive ||
                generation != _rebuildGeneration)
                return;

            if (liveTilesEnabled && cachedThumbnails is { Count: > 0 })
                EnrichWithThumbnails(summaries, cachedThumbnails);
            PublishSummaries(summaries, sortMode);
            IsLoading = false;

            if (!liveTilesEnabled || cachedThumbnails is not null)
                return;

            var thumbnails = await Task.Run(
                () => BuildThumbnailCache(
                    thumbnailCandidates ?? [],
                    cts.Token));

            if (cts.IsCancellationRequested ||
                !_isActive ||
                generation != _rebuildGeneration)
                return;

            _thumbnailCache = thumbnails;
            if (thumbnails.Count > 0)
            {
                var enrichedSummaries = summaries.ToList();
                EnrichWithThumbnails(enrichedSummaries, thumbnails);
                PublishSummaries(enrichedSummaries, sortMode);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _rebuildPending = true;
            _logger.LogError("Authors.Rebuild", ex);
        }
        finally
        {
            if (ReferenceEquals(_rebuildCts, cts))
            {
                _rebuildCts = null;
                IsLoading = false;
            }
            cts.Dispose();
        }
    }

    private static List<AuthorSummary> PrepareSummaries(
        IReadOnlyList<AuthorSortSnapshot> sourceSnapshots,
        string search,
        AuthorSortMode sortMode,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return [];
        var filtered = sourceSnapshots
            .Where(s => s.Summary.TotalCount > 0)
            .Where(s => MatchesSearch(s, search));

        var sorted = (sortMode switch
        {
            AuthorSortMode.Name => filtered
                .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase),
            AuthorSortMode.LastUpdated => filtered
                .OrderByDescending(s => s.Summary.LastUpdated)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered
                .OrderByDescending(s => s.Summary.TotalCount)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase),
        }).Select(s => s.Summary).ToList();

        return cancellationToken.IsCancellationRequested ? [] : sorted;
    }

    private List<ThumbnailCandidate> CaptureThumbnailCandidates()
    {
        var cards = _galleryViewModel.Cards;
        var candidates = new List<ThumbnailCandidate>(cards.Count);
        foreach (var card in cards)
        {
            if (card.Author is { } author)
            {
                candidates.Add(new ThumbnailCandidate(
                    author,
                    card.FilePath,
                    card.FileSize,
                    card.DateModified));
            }
        }
        return candidates;
    }

    private void PublishSummaries(
        IReadOnlyList<AuthorSummary> summaries,
        AuthorSortMode sortMode)
    {
        foreach (var tab in ProviderTabs)
        {
            var tabSummaries = summaries
                .Where(s => s.Display.Key.ProviderId.Equals(
                    tab.ProviderId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            tab.Authors = tabSummaries;

            var sortByName = sortMode == AuthorSortMode.Name;
            List<AuthorGroupViewModel> groups = tabSummaries.Count == 0
                ? []
                : sortByName
                    ? BuildNameGroups(tabSummaries)
                    : [BuildUngroupedAuthors(tabSummaries)];

            // Publish fully-built snapshots atomically. The grouped view must
            // never observe a chain of intermediate collection mutations.
            tab.Groups = new ObservableCollection<AuthorGroupViewModel>(groups);
            if (sortByName)
                SyncIndex(tab);
            else
                tab.QuickJumpItems.Clear();
            tab.AuthorCount = tabSummaries.Count;
            tab.HasAuthors = tabSummaries.Count > 0;
        }

        HasAuthors = ProviderTabs.Any(t => t.HasAuthors);
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            // Sequential on purpose: each call queues behind the plugin's rate
            // limiter anyway, and one-at-a-time gives an honest progress count.
            var keys = ProviderTabs
                .SelectMany(t => t.Authors)
                .Select(s => s.Display.Key)
                .ToList();
            for (var i = 0; i < keys.Count; i++)
            {
                RefreshProgress = $"{i + 1} / {keys.Count}";
                await _authorInfoService.RefreshAuthorAsync(keys[i]);
            }
        }
        finally
        {
            IsRefreshing = false;
            RefreshProgress = string.Empty;
        }
    }

    public Task RefreshOneAsync(AuthorSummary summary)
        => _authorInfoService.RefreshAuthorAsync(summary.Display.Key);

    private static bool MatchesSearch(AuthorSortSnapshot snapshot, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return snapshot.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
            snapshot.AuthorId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            snapshot.ProfileUrl.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static List<AuthorGroupViewModel> BuildNameGroups(IReadOnlyList<AuthorSummary> summaries)
        => summaries
            .GroupBy(s => GetGroupKey(s.Display.Name))
            .OrderBy(g => GetGroupOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var group = new AuthorGroupViewModel(g.Key, GetGroupHeader(g.Key));
                foreach (var author in g)
                    group.Authors.Add(author);
                return group;
            })
            .ToList();

    private static AuthorGroupViewModel BuildUngroupedAuthors(IReadOnlyList<AuthorSummary> summaries)
    {
        var group = new AuthorGroupViewModel("__all", string.Empty, showHeader: false);
        foreach (var author in summaries)
            group.Authors.Add(author);
        return group;
    }

    private static string GetGroupKey(string name)
    {
        var first = name.Trim().FirstOrDefault();
        if (first == default)
            return "#";

        if (char.IsAsciiLetter(first))
            return char.ToUpperInvariant(first).ToString();

        if (char.IsDigit(first))
            return "#";

        if (char.IsPunctuation(first) || char.IsSymbol(first))
            return "&";

        return "他";
    }

    private static string GetGroupHeader(string key)
        => key switch
        {
            "&" => ResLoader.GetString("Authors_GroupSymbols"),
            "他" => ResLoader.GetString("Authors_GroupOther"),
            _ => key,
        };

    private static int GetGroupOrder(string key)
    {
        if (key == "&") return -1;
        if (key == "#") return 0;
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z')
            return key[0] - 'A' + 1;
        return 100;
    }

    private static void SyncIndex(AuthorProviderTabViewModel tab)
    {
        var groupMap = tab.Groups.ToDictionary(g => g.Key, StringComparer.Ordinal);
        var keys = new[] { "&", "#" }
            .Concat(Enumerable.Range('A', 26).Select(c => ((char)c).ToString()))
            .Concat(["他"])
            .Select(k =>
            {
                groupMap.TryGetValue(k, out var group);
                return new AuthorIndexItemViewModel(k, GetIndexDisplay(k), group);
            })
            .ToList();

        SyncIndexItems(tab.QuickJumpItems, keys.Where(i => i.IsAvailable).ToList());
    }

    private static string GetIndexDisplay(string key)
        => key == "他" ? ResLoader.GetString("Authors_GroupOtherIndex") : key;

    private static void SyncIndexItems(
        ObservableCollection<AuthorIndexItemViewModel> target,
        IReadOnlyList<AuthorIndexItemViewModel> source)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (i < target.Count)
                target[i] = source[i];
            else
                target.Add(source[i]);
        }

        while (target.Count > source.Count)
            target.RemoveAt(target.Count - 1);
    }

    private const int MaxThumbnailsPerAuthor = 6;

    private static void EnrichWithThumbnails(
        List<AuthorSummary> summaries,
        IReadOnlyDictionary<AuthorDisplay, IReadOnlyList<string>> thumbnails)
    {
        for (var i = 0; i < summaries.Count; i++)
        {
            if (thumbnails.TryGetValue(summaries[i].Display, out var paths) && paths.Count > 0)
                summaries[i] = summaries[i] with { ThumbnailPaths = paths };
        }
    }

    private Dictionary<AuthorDisplay, IReadOnlyList<string>> BuildThumbnailCache(
        IReadOnlyList<ThumbnailCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var cache = _thumbnailCacheService;
        var result = new Dictionary<AuthorDisplay, IReadOnlyList<string>>();
        var temp = new Dictionary<AuthorDisplay, List<string>>();
        foreach (var candidate in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
                return result;
            if (!temp.TryGetValue(candidate.Author, out var list))
                temp[candidate.Author] = list = [];
            if (list.Count >= MaxThumbnailsPerAuthor) continue;
            var path = cache.TryGetCachedPath(
                candidate.FilePath,
                candidate.FileSize,
                candidate.DateModified);
            if (path is not null)
                list.Add(path);
        }

        foreach (var (key, list) in temp)
            result[key] = list;

        return result;
    }

    private sealed record AuthorSortSnapshot(
        AuthorSummary Summary,
        string Name,
        string AuthorId,
        string ProfileUrl);

    private sealed record ThumbnailCandidate(
        AuthorDisplay Author,
        string FilePath,
        long FileSize,
        DateTime DateModified);
}

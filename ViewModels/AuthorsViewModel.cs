using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.ApplicationModel.Resources;

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

    public ObservableCollection<AuthorSummary> Authors { get; } = [];

    public ObservableCollection<AuthorGroupViewModel> Groups { get; } = [];

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
    private static readonly TimeSpan RebuildDebounce = TimeSpan.FromMilliseconds(500);
    private static readonly ResourceLoader ResLoader = new();

    private readonly AuthorInfoService _authorInfoService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly ThumbnailCacheService _thumbnailCacheService;
    private readonly GalleryViewModel _galleryViewModel;
    private readonly DispatcherQueueTimer _rebuildTimer;
    private Dictionary<AuthorDisplay, IReadOnlyList<string>>? _thumbnailCache;

    public ObservableCollection<AuthorProviderTabViewModel> ProviderTabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasAuthors { get; set; }

    public bool IsEmpty => !HasAuthors;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => Rebuild();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSortByName))]
    public partial AuthorSortMode SortMode { get; set; }

    public bool IsSortByName => SortMode == AuthorSortMode.Name;

    partial void OnSortModeChanged(AuthorSortMode value) => Rebuild();

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
        GalleryViewModel galleryViewModel)
    {
        _authorInfoService = authorInfoService;
        _settingsViewModel = settingsViewModel;
        _thumbnailCacheService = thumbnailCacheService;
        _galleryViewModel = galleryViewModel;
        foreach (var provider in _authorInfoService.ProviderInfos)
            ProviderTabs.Add(new AuthorProviderTabViewModel(provider));

        _rebuildTimer = dispatcher.CreateTimer();
        _rebuildTimer.Interval = RebuildDebounce;
        _rebuildTimer.IsRepeating = false;
        _rebuildTimer.Tick += (_, _) => Rebuild();

        _authorInfoService.AuthorsChanged += () =>
        {
            _thumbnailCache = null;
            _rebuildTimer.Start();
        };
        Rebuild();
    }

    private void Rebuild()
    {
        var search = SearchText.Trim();
        var filtered = _authorInfoService.GetSummaries()
            .Where(s => s.TotalCount > 0)
            .Where(s => MatchesSearch(s, search));

        var sorted = (SortMode switch
        {
            AuthorSortMode.Name => filtered
                .OrderBy(s => s.Display.Name, StringComparer.CurrentCultureIgnoreCase),
            AuthorSortMode.LastUpdated => filtered
                .OrderByDescending(s => s.LastUpdated)
                .ThenBy(s => s.Display.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered
                .OrderByDescending(s => s.TotalCount)
                .ThenBy(s => s.Display.Name, StringComparer.CurrentCultureIgnoreCase),
        }).ToList();

        var summaries = EnrichWithThumbnails(sorted);

        foreach (var tab in ProviderTabs)
        {
            var tabSummaries = summaries
                .Where(s => s.Display.Key.ProviderId.Equals(tab.ProviderId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < tabSummaries.Count; i++)
            {
                if (i < tab.Authors.Count)
                {
                    if (SummaryChanged(tab.Authors[i], tabSummaries[i]))
                        tab.Authors[i] = tabSummaries[i];
                }
                else
                    tab.Authors.Add(tabSummaries[i]);
            }
            while (tab.Authors.Count > tabSummaries.Count)
                tab.Authors.RemoveAt(tab.Authors.Count - 1);

            var sortByName = SortMode == AuthorSortMode.Name;

            List<AuthorGroupViewModel> groups = tabSummaries.Count == 0
                ? []
                : sortByName
                    ? BuildNameGroups(tabSummaries)
                    : [BuildUngroupedAuthors(tabSummaries)];

            SyncGroups(tab.Groups, groups);
            if (sortByName)
                SyncIndex(tab);
            else
                tab.QuickJumpItems.Clear();
            tab.AuthorCount = tab.Authors.Count;
            tab.HasAuthors = tab.Authors.Count > 0;
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

    private static bool MatchesSearch(AuthorSummary summary, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return summary.Display.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
            summary.Display.Key.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            summary.Display.ProfileUrl.Contains(search, StringComparison.OrdinalIgnoreCase);
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

    private static void SyncGroups(
        ObservableCollection<AuthorGroupViewModel> target,
        IReadOnlyList<AuthorGroupViewModel> source)
    {
        // Check if the group keys are identical — if so, update in place.
        // Otherwise clear-and-rebuild to avoid Replace events that trigger
        // re-entrant layout crashes inside the Pivot's ListView.
        bool sameStructure = target.Count == source.Count;
        if (sameStructure)
        {
            for (var i = 0; i < target.Count; i++)
            {
                if (target[i].Key != source[i].Key) { sameStructure = false; break; }
            }
        }

        if (sameStructure)
        {
            for (var i = 0; i < source.Count; i++)
            {
                SyncAuthors(target[i].Authors, source[i].Authors);
                target[i].ShowHeader = source[i].ShowHeader;
                target[i].NotifyCountChanged();
            }
            return;
        }

        target.Clear();
        foreach (var group in source)
            target.Add(group);
    }

    private static bool SummaryChanged(AuthorSummary a, AuthorSummary b) =>
        !ReferenceEquals(a.Display, b.Display) ||
        a.SceneCount != b.SceneCount ||
        a.CharacterCount != b.CharacterCount ||
        a.CoordinateCount != b.CoordinateCount;

    private static void SyncAuthors(
        ObservableCollection<AuthorSummary> target,
        IReadOnlyList<AuthorSummary> source)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (i < target.Count)
            {
                if (SummaryChanged(target[i], source[i]))
                    target[i] = source[i];
            }
            else
                target.Add(source[i]);
        }

        while (target.Count > source.Count)
            target.RemoveAt(target.Count - 1);
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

    private List<AuthorSummary> EnrichWithThumbnails(List<AuthorSummary> summaries)
    {
        if (!_settingsViewModel.AuthorLiveTilesEnabled)
            return summaries;

        var thumbsByAuthor = _thumbnailCache ?? BuildThumbnailCache();
        if (thumbsByAuthor.Count == 0)
            return summaries;

        for (var i = 0; i < summaries.Count; i++)
        {
            if (thumbsByAuthor.TryGetValue(summaries[i].Display, out var paths) && paths.Count > 0)
                summaries[i] = summaries[i] with { ThumbnailPaths = paths };
        }

        return summaries;
    }

    private Dictionary<AuthorDisplay, IReadOnlyList<string>> BuildThumbnailCache()
    {
        var cache = _thumbnailCacheService;
        var cards = _galleryViewModel.Cards;
        var result = new Dictionary<AuthorDisplay, IReadOnlyList<string>>();

        if (cards.Count == 0)
        {
            _thumbnailCache = result;
            return result;
        }

        var temp = new Dictionary<AuthorDisplay, List<string>>();
        foreach (var card in cards)
        {
            if (card.Author is not { } author) continue;
            if (!temp.TryGetValue(author, out var list))
                temp[author] = list = [];
            if (list.Count >= MaxThumbnailsPerAuthor) continue;
            var path = cache.TryGetCachedPath(card);
            if (path is not null)
                list.Add(path);
        }

        foreach (var (key, list) in temp)
            result[key] = list;

        _thumbnailCache = result;
        return result;
    }
}

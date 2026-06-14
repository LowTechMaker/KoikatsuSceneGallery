using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.ViewModels;

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

    private readonly AuthorInfoService _authorInfoService;
    private readonly DispatcherQueueTimer _rebuildTimer;

    public ObservableCollection<AuthorProviderTabViewModel> ProviderTabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasAuthors { get; set; }

    public bool IsEmpty => !HasAuthors;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRefreshing))]
    public partial bool IsRefreshing { get; set; }

    public bool IsNotRefreshing => !IsRefreshing;

    /// <summary>Numeric "done / total" progress while a refresh-all runs.</summary>
    [ObservableProperty]
    public partial string RefreshProgress { get; set; } = string.Empty;

    public AuthorsViewModel(AuthorInfoService authorInfoService, DispatcherQueue dispatcher)
    {
        _authorInfoService = authorInfoService;
        foreach (var provider in _authorInfoService.ProviderInfos)
            ProviderTabs.Add(new AuthorProviderTabViewModel(provider));

        _rebuildTimer = dispatcher.CreateTimer();
        _rebuildTimer.Interval = RebuildDebounce;
        _rebuildTimer.IsRepeating = false;
        _rebuildTimer.Tick += (_, _) => Rebuild();

        _authorInfoService.AuthorsChanged += () => _rebuildTimer.Start();
        Rebuild();
    }

    private void Rebuild()
    {
        var summaries = _authorInfoService.GetSummaries()
            .Where(s => s.TotalCount > 0)
            .OrderByDescending(s => s.TotalCount)
            .ThenBy(s => s.Display.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var tab in ProviderTabs)
        {
            var tabSummaries = summaries
                .Where(s => s.Display.Key.ProviderId.Equals(tab.ProviderId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < tabSummaries.Count; i++)
            {
                if (i < tab.Authors.Count)
                    tab.Authors[i] = tabSummaries[i];
                else
                    tab.Authors.Add(tabSummaries[i]);
            }
            while (tab.Authors.Count > tabSummaries.Count)
                tab.Authors.RemoveAt(tab.Authors.Count - 1);

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
}

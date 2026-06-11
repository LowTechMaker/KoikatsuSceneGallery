using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.ViewModels;

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

    public ObservableCollection<AuthorSummary> Authors { get; } = [];

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

        for (int i = 0; i < summaries.Count; i++)
        {
            if (i < Authors.Count)
                Authors[i] = summaries[i];
            else
                Authors.Add(summaries[i]);
        }
        while (Authors.Count > summaries.Count)
            Authors.RemoveAt(Authors.Count - 1);

        HasAuthors = Authors.Count > 0;
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
            var keys = Authors.Select(s => s.Display.Key).ToList();
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

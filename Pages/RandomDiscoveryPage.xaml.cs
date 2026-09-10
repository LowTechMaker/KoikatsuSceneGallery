using System.Collections.Specialized;
using System.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class RandomDiscoveryPage : Page
{
    private readonly GalleryViewModel _gallery = App.Services.GetRequiredService<GalleryViewModel>();
    private readonly SettingsViewModel _settings = App.Services.GetRequiredService<SettingsViewModel>();
    private readonly DiscoveryShuffle<string> _shuffle = new(Random.Shared, StringComparer.OrdinalIgnoreCase);
    private readonly DiscoveryCollection _items;
    private readonly GalleryLayoutEngine _layout;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _searchTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _refreshTimer;
    private Dictionary<string, SceneCard> _candidates = new(StringComparer.OrdinalIgnoreCase);
    private SceneDiscoveryFilter _filter = new([], true, GameFilterOption.All, false, []);
    private string[] _folders = [];
    private bool _initialized, _active, _syncing, _loading, _restorePosition, _reloadRequested;
    private string _appliedQuery = "";
    private double _offset;
    private int _returnIndex = -1;

    public RandomDiscoveryPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _items = new(DispatcherQueue, () => _active && _shuffle.Remaining > 0, Append);
        DiscoveryGrid.ItemsSource = _items;
        AutomationProperties.SetName(DiscoveryGrid, UiText.Get("Discovery_Title.Text"));
        AutomationProperties.SetName(SearchBox, SearchBox.PlaceholderText);
        _layout = new(135.0 / 240, DiscoveryGrid, DispatcherQueue, _ => { }, _settings, 62);
        _searchTimer = DispatcherQueue.CreateTimer();
        _searchTimer.Interval = TimeSpan.FromMilliseconds(250);
        _searchTimer.IsRepeating = false;
        _searchTimer.Tick += (_, _) => ApplyConditions();
        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(150);
        _refreshTimer.IsRepeating = false;
        _refreshTimer.Tick += (_, _) => RefreshCandidates();
        foreach (var name in new[] { UiText.Get("Gallery_FilterAll.Content"), "Koikatsu", "Koikatsu Sunshine", UiText.Get("Gallery_EnvUnknown.Content") })
            GameFilter.Items.Add(name);
        Loaded += (_, _) =>
        {
            _layout.OnLoaded(RequestVisible);
            DispatcherQueue.TryEnqueue(RestorePosition);
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        _gallery.Cards.CollectionChanged += Cards_Changed;
        _gallery.CardsReloaded += QueueRefresh;
        _gallery.ViewRefreshed += QueueRefresh;
        _gallery.PropertyChanged += Gallery_Changed;
        _settings.SceneFolderPathsChanged += Folders_Changed;
        _layout.BaseItemWidth = _settings.ThumbnailSize.ToPixels();
        _gallery.Activate();
        if (!_initialized)
        {
            _syncing = true;
            SearchBox.Text = _gallery.SearchText;
            RatingFilter.IsOn = _gallery.ShowR18Content;
            GameFilter.SelectedIndex = (int)_gallery.GameFilter;
            ResolutionFilter.IsOn = _settings.ResolutionFilterEnabled;
            _syncing = false;
            _initialized = true;
            CaptureFilter();
            _shuffle.Reset([]);
        }
        bool reload = !_folders.SequenceEqual(_settings.FolderPaths, StringComparer.OrdinalIgnoreCase);
        _folders = _settings.FolderPaths.ToArray();
        UpdateStatus();
        if (reload || _reloadRequested || _gallery.Cards.Count == 0) Load();
        else if (SearchBox.Text != _appliedQuery) ApplyConditions();
        else RefreshCandidates();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _offset = VisualTreeSearch.FindDescendant<ScrollViewer>(DiscoveryGrid)?.VerticalOffset ?? 0;
        _restorePosition = true;
        _reloadRequested |= _loading || _gallery.IsLoading;
        _active = false;
        BusyRing.IsActive = false;
        _items.Invalidate();
        _searchTimer.Stop();
        _refreshTimer.Stop();
        _gallery.Cards.CollectionChanged -= Cards_Changed;
        _gallery.CardsReloaded -= QueueRefresh;
        _gallery.ViewRefreshed -= QueueRefresh;
        _gallery.PropertyChanged -= Gallery_Changed;
        _settings.SceneFolderPathsChanged -= Folders_Changed;
        _gallery.CancelPendingWork();
        base.OnNavigatedFrom(e);
    }

    private void CaptureFilter()
    {
        _appliedQuery = SearchBox.Text;
        _filter = new(SearchBox.Text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray(),
            RatingFilter.IsOn, (GameFilterOption)Math.Max(0, GameFilter.SelectedIndex),
            ResolutionFilter.IsOn, new(_settings.AllowedResolutions));
    }

    private void ApplyConditions()
    {
        if (!_active || _syncing) return;
        _searchTimer.Stop();
        CaptureFilter();
        Restart();
    }

    private void Restart()
    {
        _items.Invalidate();
        _items.Clear();
        _returnIndex = -1;
        _offset = 0;
        _restorePosition = false;
        _shuffle.Reset([]);
        RefreshCandidates();
        VisualTreeSearch.FindDescendant<ScrollViewer>(DiscoveryGrid)?.ChangeView(null, 0, null, true);
    }

    private void RefreshCandidates()
    {
        if (!_active) { UpdateStatus(); return; }
        // Draw from whatever has been scanned so far: waiting for the whole library
        // made the first card appear only after the full scan and cache write.
        _candidates = _gallery.Cards.Where(_filter.Matches)
            .DistinctBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(c => c.FilePath, StringComparer.OrdinalIgnoreCase);
        bool wasExhausted = _shuffle.Remaining == 0;
        _shuffle.Synchronize(_candidates.Keys);
        // A scan can replace card objects. Keep positions, but use their current metadata.
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (!_candidates.TryGetValue(_items[i].Card.FilePath, out var current)) _items.RemoveAt(i);
            else if (!ReferenceEquals(_items[i].Card, current)) _items[i] = new(current, _items[i].Round);
        }
        if (_items.Count == 0 || (wasExhausted && _shuffle.Remaining > 0)) Append(40);
        UpdateStatus();
        _layout.EnsureLayoutOnFirstContent();
        RequestVisible();
    }

    private int Append(int count)
    {
        var paths = _shuffle.Take(count);
        foreach (var path in paths) _items.Add(new(_candidates[path], _shuffle.Round));
        UpdateStatus();
        return paths.Count;
    }

    private void UpdateStatus()
    {
        bool busy = _loading || _gallery.IsLoading;
        bool analyzing = _gallery.IsParsingMetadata;
        BusyPanel.Visibility = busy || analyzing ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.IsActive = _active && (busy || analyzing);
        BusyText.Text = UiText.Get(busy ? "Browser_Loading" : "Gallery_AnalyzingLabel.Text");
        ProgressText.Text = UiText.Format("Discovery_Progress", Math.Max(1, _shuffle.Round), _shuffle.Drawn, _shuffle.Total);
        ResetButton.IsEnabled = _candidates.Count > 0;
        bool complete = !busy && _shuffle.Total > 0 && _shuffle.Remaining == 0;
        RoundFooter.Visibility = complete ? Visibility.Visible : Visibility.Collapsed;
        var endText = UiText.Format("Discovery_RoundEnd", _shuffle.Round);
        if (RoundEndText.Text != endText)
        {
            RoundEndText.Text = endText;
            if (complete) FrameworkElementAutomationPeer.FromElement(RoundEndText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        EmptyPanel.Visibility = !busy && !ErrorBar.IsOpen && _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool noLibrary = _gallery.Cards.Count == 0;
        EmptyTitle.Text = UiText.Get(noLibrary ? "SceneGallery_NoLibraryTitle" : "SceneGallery_NoResultsTitle");
        EmptyDescription.Text = UiText.Get(noLibrary ? "SceneGallery_NoLibraryDescription" : "SceneGallery_NoResultsDescription");
        ClearButton.Visibility = noLibrary ? Visibility.Collapsed : Visibility.Visible;
        GameFilter.Visibility = _gallery.ShowMetadataFilters ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Load()
    {
        if (!_active) return;
        if (_loading) { _reloadRequested = true; return; }
        _reloadRequested = false;
        UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Discovery.Load", async () =>
        {
            _loading = true;
            ErrorBar.IsOpen = false;
            UpdateStatus();
            try { await _gallery.LoadCardsCommand.ExecuteAsync(null); }
            catch (Exception ex)
            {
                App.Services.GetRequiredService<IAppLogger>().LogError("Discovery.Load", ex);
                ErrorBar.IsOpen = true;
            }
            finally
            {
                _loading = false;
                if (_active)
                {
                    if (_reloadRequested) Load();
                    else if (SearchBox.Text != _appliedQuery) ApplyConditions();
                    else RefreshCandidates();
                }
            }
        });
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        int firstNew = _items.Count;
        _items.Invalidate();
        _shuffle.StartRound(_candidates.Keys);
        Append(40);
        // Preserve previous rounds; move keyboard focus to the first newly appended card.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || firstNew >= _items.Count) return;
            DiscoveryGrid.ScrollIntoView(_items[firstNew]);
            if (DiscoveryGrid.ContainerFromIndex(firstNew) is GridViewItem item) item.Focus(FocusState.Programmatic);
        });
    }

    private void Item_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DiscoveryItem entry) return;
        _returnIndex = _items.IndexOf(entry);
        new BrowseContext(UiText.Get("Discovery_Title.Text"), _items.Select(item => item.Card)).Open(Frame, entry.Card);
    }

    private void RestorePosition()
    {
        if (!_active) return;
        if (_restorePosition)
        {
            _restorePosition = false;
            VisualTreeSearch.FindDescendant<ScrollViewer>(DiscoveryGrid)?.ChangeView(null, _offset, null, true);
            if (_returnIndex >= 0 && DiscoveryGrid.ContainerFromIndex(_returnIndex) is GridViewItem item)
                item.Focus(FocusState.Programmatic);
        }
        RequestVisible();
    }

    private void Container_Changing(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not DiscoveryItem entry) return;
        var card = entry.Card;
        if (args.InRecycleQueue) _gallery.ReleaseThumbnail(card);
        else if (_active)
        {
            AutomationProperties.SetName(args.ItemContainer, card.FileName);
            _gallery.RequestThumbnail(card);
        }
        _layout?.EnsureLayoutOnFirstContent();
    }

    private void RequestVisible()
    {
        if (!_active || DiscoveryGrid.ItemsPanelRoot is not ItemsWrapGrid panel) return;
        for (int i = Math.Max(0, panel.FirstVisibleIndex); i <= panel.LastVisibleIndex && i < _items.Count; i++)
            _gallery.RequestThumbnail(_items[i].Card, ThumbnailWorkPriority.Visible);
    }

    private void Drag_Starting(object sender, DragItemsStartingEventArgs e)
        => DragFilePayload.Attach(e, "Discovery.Drag", e.Items.OfType<DiscoveryItem>().Select(item => item.Card.FilePath));

    private void QueueRefresh()
    {
        if (!_active || _refreshTimer.IsRunning) return;
        // Rebuilding the candidate pool is O(cards); back off while a scan floods the collection.
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(_loading || _gallery.IsLoading ? 400 : 150);
        _refreshTimer.Start();
    }
    private void Cards_Changed(object? sender, NotifyCollectionChangedEventArgs e) => QueueRefresh();
    private void Gallery_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.IsLoading) or nameof(GalleryViewModel.IsParsingMetadata)
            or nameof(GalleryViewModel.ShowMetadataFilters)) QueueRefresh();
    }
    private void Folders_Changed() { _folders = _settings.FolderPaths.ToArray(); Load(); }
    private void Search_Changed(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (_syncing || !_active || e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _searchTimer.Stop(); _searchTimer.Start();
    }
    private void Search_Submitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e) => ApplyConditions();
    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyConditions();
    private void Game_Changed(object sender, SelectionChangedEventArgs e) => ApplyConditions();
    private void Reset_Click(object sender, RoutedEventArgs e) => ApplyConditions();
    private void Retry_Click(object sender, RoutedEventArgs e) => Load();
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        SearchBox.Text = "";
        RatingFilter.IsOn = true;
        GameFilter.SelectedIndex = 0;
        ResolutionFilter.IsOn = false;
        _syncing = false;
        ApplyConditions();
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage), "filters");
    private void Search_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
    { SearchBox.Focus(FocusState.Keyboard); e.Handled = true; }
    private void Top_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
    { VisualTreeSearch.FindDescendant<ScrollViewer>(DiscoveryGrid)?.ChangeView(null, 0, null, true); e.Handled = true; }
    private void Toolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 640;
        Grid.SetRow(Actions, narrow ? 1 : 0);
        Grid.SetColumn(Actions, narrow ? 0 : 1);
        Grid.SetColumnSpan(SearchBox, narrow ? 2 : 1);
        Grid.SetColumnSpan(Actions, narrow ? 2 : 1);
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.WinUI.Collections;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Pages;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.System;

namespace KoikatsuSceneGallery.Controls;

public sealed partial class LibraryBrowser : UserControl
{
    private LibraryAdapter? _adapter;
    private IReadOnlyList<CardBase>? _scope;
    private string? _scopeTitle;
    private string _localQuery = "";
    private SortOption _localSort;
    private bool _localAscending = true;
    private readonly Dictionary<CardBase, int> _shuffleOrder = [];
    public GridView ItemsGrid => GalleryGrid;
    public event Action? OpeningDetail;
    private string MainQuery => _scope is null ? VM.SearchText : _localQuery;
    private GalleryViewModelBase VM => _adapter!.ViewModel;
    private readonly ObservableCollection<GalleryEntry> _items = [];
    private readonly AdvancedCollectionView _view;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _refresh;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _search;
    private readonly Dictionary<(CardBase, string?, string?), string> _keys = [];
    private GalleryLayoutEngine? _layout;
    private Frame? _frame;
    private bool _active, _reload, _syncing, _entriesDetached;
    private string? _groupKey, _groupTitle, _returnPath;
    private double _returnOffset;
    private int _returnIndex;
    private string _groupSearch = "";
    private BrowseContext? _detailContext;
    private readonly SettingsViewModel _settings = App.Services.GetRequiredService<SettingsViewModel>();
    public LibraryBrowser()
    {
        InitializeComponent();
        _view = new AdvancedCollectionView(_items, false);
        GalleryGrid.ItemsSource = _view;
        _refresh = DispatcherQueue.CreateTimer(); _refresh.Interval = TimeSpan.FromMilliseconds(80); _refresh.IsRepeating = false;
        _refresh.Tick += (_, _) => Refresh();
        _search = DispatcherQueue.CreateTimer(); _search.Interval = TimeSpan.FromMilliseconds(200); _search.IsRepeating = false;
        _search.Tick += (_, _) => ApplySearch();
        Loaded += (_, _) => _layout?.OnLoaded(RequestVisible);
    }
    public static BitmapImage? Thumbnail(Uri? uri) => uri is null ? null : new() { DecodePixelWidth = 400, UriSource = uri };
    public void Initialize(LibraryKind kind, IReadOnlyList<CardBase>? scope = null, string? title = null)
    {
        _scope = scope; _scopeTitle = title;
        _adapter = new(kind);
        _layout = new(_adapter.ImageRatio, GalleryGrid, DispatcherQueue, count => { if (_scope is null) VM.SetShuffleDisplayCount(count); }, _settings, 58);
        if (_scope is not null) { RootLayout.Padding = new Thickness(0,12,0,0); FilterButton.Visibility = Visibility.Collapsed; return; }
        VM.CardsView.VectorChanged += (_, _) => QueueRefresh();
        VM.ViewRefreshed += QueueRefresh;
        VM.CardsReloaded += () => { _keys.Clear(); QueueRefresh(); };
        VM.PropertyChanged += (_, e) => { if (e.PropertyName is not "PendingThumbnailCount") QueueRefresh(); };
        _adapter.WatchFolders(() => { _reload = true; if (_active) Load(); });
        _settings.ThumbnailSizeChanged += preference => { if (_active) UpdateSize(preference); };
        BuildFilters();
    }
    public void Activate(Frame frame)
    {
        _frame = frame; _active = true; if (_scope is null) VM.Activate();
        _syncing = true; SortBox.SelectedIndex = (int)(_scope is null ? VM.SelectedSort : _localSort); _syncing = false;
        UpdateSize(_settings.ThumbnailSize);
        SetSearchText(_groupKey is null ? MainQuery : _groupSearch);
        Refresh();
        if (_scope is null && (_adapter!.Cards.Count == 0 || _reload)) Load();
        else if (_detailContext?.ReturnPath is { } path) RestoreItem(path);
    }
    public void Deactivate()
    {
        ApplySearch(); _active = false; _refresh.Stop(); _search.Stop(); if (_scope is null) VM.CancelPendingWork();
        foreach (var item in _items) { _adapter?.Thumbnail(item.Card, true); item.Dispose(); }
        _entriesDetached = true;
    }
    private void Load()
    {
        _reload = false;
        UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Browser.Load", async () =>
        { await _adapter!.LoadAsync(); QueueRefresh(); });
    }
    private void QueueRefresh() { if (_active && !_refresh.IsRunning) _refresh.Start(); }
    private string Key(CardBase card)
    {
        var author = (card as IAuthorOwner)?.Author;
        var cacheKey = (card, author?.Key.ProviderId, author?.Key.Id);
        if (_keys.TryGetValue(cacheKey, out var key)) return key;
        try
        {
            var provider = App.Services.GetRequiredService<PluginService>().ImportProviders
                .FirstOrDefault(p => p.ProviderId == author?.Key.ProviderId);
            key = GalleryGrouping.ResolveKey(card.FilePath, provider?.ProviderId,
                provider?.TryParseArtworkFolderName(Path.GetFileName(Path.GetDirectoryName(card.FilePath)) ?? "")?.Id,
                provider?.TryParseFilename(card.FileName)?.Id, author?.Key.Id);
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<IAppLogger>().LogError("Browser.GroupIdentity", ex, card.FilePath);
            key = "folder:" + Path.GetDirectoryName(card.FilePath);
        }
        return _keys[cacheKey] = key;
    }
    private void Refresh()
    {
        _refresh.Stop(); if (!_active || _adapter is null) return;
        var cards = _scope is null ? VM.CardsView.OfType<CardBase>().ToArray() : ScopedCards();
        var visible = _groupKey is null ? cards : cards.Where(c => string.Equals(Key(c), _groupKey, StringComparison.OrdinalIgnoreCase)).ToArray();
        var groupTotal = visible.Length;
        if (_groupKey is not null)
        {
            var keywords = _groupSearch.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToArray();
            visible = visible.Where(c => GallerySearch.Matches(c.FilePath, (c as IAuthorOwner)?.Author?.Name, keywords)).ToArray();
        }
        var groups = _groupKey is null && _adapter.GroupingEnabled ? GalleryGrouping.Create(visible, Key)
            : visible.Select(c => new GalleryGroup<CardBase>(Key(c), [c])).ToArray();
        bool changed = _entriesDetached || groups.Count != _items.Count || groups.Where((g, i) =>
            g.Key != _items[i].Key || !_items[i].Members.SequenceEqual(g.Members) || _items[i].ShowTitle != VM.ShowFileNames).Any();
        if (changed)
        {
            _entriesDetached = false;
            using (_view.DeferRefresh())
            {
                foreach (var item in _items) item.Dispose();
                _items.Clear();
                foreach (var group in groups) _items.Add(new(group.Key, group.Members, VM.ShowFileNames));
            }
        }
        Heading.Text = _groupKey is null ? _adapter.Title : _groupTitle;
        BackButton.Visibility = _groupKey is null ? Visibility.Collapsed : Visibility.Visible;
        ResultCount.Text = UiText.Format(visible.Length == (_groupKey is null ? (_scope?.Count ?? _adapter.Cards.Count) : groupTotal)
            ? "SceneGallery_TotalCount" : "SceneGallery_FilteredCount", visible.Length, _groupKey is null ? _adapter.Cards.Count : groupTotal);
        SearchBox.PlaceholderText = UiText.Get(_groupKey is null ? "SceneGallery_Search.PlaceholderText" : "Browser_GroupSearch");
        AutomationProperties.SetName(SearchBox, SearchBox.PlaceholderText);
        int filterCount = VM switch { GalleryViewModel vm => (vm.ShowR18Content ? 0 : 1) + (vm.GameFilter == GameFilterOption.All ? 0 : 1), CharacterGalleryViewModel vm => vm.SourceFilter == CardSourceFilterOption.All ? 0 : 1, _ => 0 };
        FilterLabel.Text = filterCount == 0 ? UiText.Get("SceneGallery_Filters") : UiText.Format("SceneGallery_ActiveFilters", filterCount);
        RandomButton.IsEnabled = visible.Length > 0;
        DirectionButton.IsEnabled = _scope is null ? !VM.IsShuffleMode : _localSort != SortOption.Shuffle;
        DirectionButton.IsChecked = !(_scope is null ? VM.SortAscending : _localAscending);
        DirectionIcon.Glyph = (_scope is null ? VM.SortAscending : _localAscending) ? "\uE74A" : "\uE74B";
        bool busy = VM.IsLoading || VM.IsGeneratingThumbnails || _adapter.IsParsing;
        BusyStatus.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; BusyRing.IsActive = busy;
        BusyText.Text = UiText.Get(VM.IsLoading ? "Browser_Loading" : _adapter.IsParsing ? "Gallery_AnalyzingLabel.Text" : "Gallery_GeneratingLabel.Text");
        EmptyPanel.Visibility = visible.Length == 0 && !VM.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = UiText.Get(_adapter.Cards.Count == 0 ? "SceneGallery_NoLibraryTitle" : "SceneGallery_NoResultsTitle");
        EmptyDescription.Text = UiText.Get(_groupKey is not null ? "SceneGallery_GroupEmpty.Text" : _adapter.Cards.Count == 0 ? "SceneGallery_NoLibraryDescription" : "SceneGallery_NoResultsDescription");
        ClearButton.Content = UiText.Get(_groupKey is null ? "SceneGallery_ResetSearch.Content" : "Browser_ClearGroupSearch");
        ClearButton.Visibility = _adapter.Cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _layout?.SetMetadataReserve(VM.ShowFileNames ? 58 : 38);
        DispatcherQueue.TryEnqueue(RequestVisible);
    }
    private CardBase[] ScopedCards()
    {
        var keywords = _localQuery.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToArray();
        var cards = _scope!.Where(c => GallerySearch.Matches(c.FilePath, (c as IAuthorOwner)?.Author?.Name, keywords));
        foreach (var card in _scope!) if (!_shuffleOrder.ContainsKey(card)) _shuffleOrder[card] = Random.Shared.Next();
        IEnumerable<CardBase> ordered = _localSort switch
        {
            SortOption.DateModified => cards.OrderBy(c => c.DateModified),
            SortOption.FileSize => cards.OrderBy(c => c.FileSize),
            SortOption.Shuffle => cards.OrderBy(c => _shuffleOrder[c]),
            _ => cards.OrderBy(c => c.FileName, StringComparer.CurrentCultureIgnoreCase)
        };
        return (_localAscending || _localSort == SortOption.Shuffle ? ordered : ordered.Reverse()).ToArray();
    }
    public BrowserState SaveState() => new(_localQuery, _groupKey, _groupTitle, _groupSearch, _returnPath, _returnIndex, _returnOffset, _localSort, _localAscending, _detailContext);
    public void RestoreState(BrowserState state)
    {
        _localQuery = state.Query; _groupKey = state.GroupKey; _groupTitle = state.GroupTitle;
        _groupSearch = state.GroupSearch; _returnPath = state.ReturnPath; _returnIndex = state.ReturnIndex;
        _returnOffset = state.ReturnOffset; _localSort = state.Sort; _localAscending = state.Ascending;
        _detailContext = state.Context; SortBox.SelectedIndex = (int)_localSort;
    }
    private void BuildFilters()
    {
        if (VM is GalleryViewModel scene)
        {
            var rating = new ToggleSwitch { Header = UiText.Get("SceneGallery_Rating.Header"), IsOn = scene.ShowR18Content, Visibility = scene.ShowR18FilterButton ? Visibility.Visible : Visibility.Collapsed };
            rating.Toggled += (_, _) => scene.ShowR18Content = rating.IsOn;
            scene.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(scene.ShowR18Content)) rating.IsOn = scene.ShowR18Content; if (e.PropertyName == nameof(scene.ShowR18FilterButton)) rating.Visibility = scene.ShowR18FilterButton ? Visibility.Visible : Visibility.Collapsed; };
            SpecificFilters.Children.Add(rating);
            var game = new ComboBox { Header = UiText.Get("SceneGallery_Environment.Header"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var s in new[] { UiText.Get("Gallery_FilterAll.Content"), "Koikatsu", "Koikatsu Sunshine", UiText.Get("Gallery_EnvUnknown.Content") }) game.Items.Add(s);
            game.SelectedIndex = (int)scene.GameFilter; game.Visibility = scene.ShowMetadataFilters ? Visibility.Visible : Visibility.Collapsed;
            game.SelectionChanged += (_, _) => { if (game.SelectedIndex >= 0) scene.GameFilter = (GameFilterOption)game.SelectedIndex; };
            scene.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(scene.ShowMetadataFilters)) game.Visibility = scene.ShowMetadataFilters ? Visibility.Visible : Visibility.Collapsed; if (e.PropertyName == nameof(scene.GameFilter)) game.SelectedIndex = (int)scene.GameFilter; };
            SpecificFilters.Children.Add(game);
        }
        if (VM is CharacterGalleryViewModel character)
        {
            var source = new ComboBox { Header = UiText.Get("Browser_Source"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var s in new[] { UiText.Get("Gallery_FilterAll.Content"), "Koikatsu Sunshine", "Koikatsu HF", "Madevil", UiText.Get("Gallery_EnvUnknown.Content") }) source.Items.Add(s);
            source.SelectedIndex = (int)character.SourceFilter;
            source.SelectionChanged += (_, _) => { if (source.SelectedIndex >= 0) character.SourceFilter = (CardSourceFilterOption)source.SelectedIndex; };
            character.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(character.SourceFilter)) source.SelectedIndex = (int)character.SourceFilter; };
            SpecificFilters.Children.Add(source);
        }
    }
    private void SetSearchText(string text) { _syncing = true; SearchBox.Text = text; _syncing = false; }
    private void Search_Changed(AutoSuggestBox s, AutoSuggestBoxTextChangedEventArgs e) { if (_syncing) return; _search.Stop(); _search.Start(); }
    private void ApplySearch()
    {
        _search.Stop(); if (_adapter is null) return;
        if (_groupKey is not null) _groupSearch = SearchBox.Text;
        else if (_scope is null) VM.SearchText = SearchBox.Text;
        else _localQuery = SearchBox.Text;
        Refresh();
    }
    private void Search_Submitted(AutoSuggestBox s, AutoSuggestBoxQuerySubmittedEventArgs e) { ApplySearch(); ScrollFirst(); }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SetSearchText("");
        if (_scope is null && _groupKey is null) { if (VM is GalleryViewModel s) s.GameFilter = GameFilterOption.All; if (VM is CharacterGalleryViewModel c) c.SourceFilter = CardSourceFilterOption.All; }
        ApplySearch(); SearchBox.Focus(FocusState.Keyboard);
    }
    private void Item_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GalleryEntry entry) return;
        if (entry.IsGroup)
        {
            ApplySearch(); _returnPath = entry.Card.FilePath; _returnIndex = _items.IndexOf(entry);
            _returnOffset = VisualTreeSearch.FindDescendant<ScrollViewer>(GalleryGrid)?.VerticalOffset ?? 0;
            _groupKey = entry.Key; _groupTitle = entry.Title; _groupSearch = ""; SetSearchText(""); Refresh(); ScrollFirst();
            BackButton.Focus(FocusState.Keyboard);
        }
        else OpenDetail(entry.Card);
    }
    private void OpenDetail(CardBase card)
    {
        _detailContext = new(_scopeTitle is null ? Heading.Text : $"{_scopeTitle} › {Heading.Text}", _items.SelectMany(i => i.Members), _groupKey is not null);
        OpeningDetail?.Invoke();
        _detailContext.Open(_frame!, card);
    }
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _groupKey = null; _groupSearch = ""; SetSearchText(MainQuery); Refresh();
        RestoreItem(_returnPath, _returnIndex, _returnOffset);
    }
    private void RestoreItem(string? path, int fallback = 0, double? offset = null)
    {
        var item = _items.FirstOrDefault(i => i.Members.Any(c => c.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            ?? (_items.Count > 0 ? _items[Math.Clamp(fallback, 0, _items.Count - 1)] : null);
        if (item is null) return;
        GalleryGrid.ScrollIntoView(item);
        DispatcherQueue.TryEnqueue(() =>
        {
            GalleryGrid.UpdateLayout();
            if (offset is not null) VisualTreeSearch.FindDescendant<ScrollViewer>(GalleryGrid)?.ChangeView(null, offset, null, true);
            (GalleryGrid.ContainerFromItem(item) as Control)?.Focus(FocusState.Keyboard);
        });
    }
    private void Random_Click(object sender, RoutedEventArgs e) { var cards = _items.SelectMany(i => i.Members).ToArray(); if (cards.Length > 0) OpenDetail(cards[Random.Shared.Next(cards.Length)]); }
    private void Sort_Changed(object sender, SelectionChangedEventArgs e) { if (_syncing || _adapter is null || SortBox.SelectedIndex < 0) return; if (_scope is null) VM.SelectedSort = (SortOption)SortBox.SelectedIndex; else { _localSort = (SortOption)SortBox.SelectedIndex; _shuffleOrder.Clear(); } Refresh(); if (_settings.ScrollToTopOnSort) ScrollFirst(); }
    private void Direction_Click(object sender, RoutedEventArgs e) { if (_scope is null) VM.SortAscending = !DirectionButton.IsChecked.GetValueOrDefault(); else _localAscending = !DirectionButton.IsChecked.GetValueOrDefault(); Refresh(); if (_settings.ScrollToTopOnSort) ScrollFirst(); }
    private void Size_Changed(object sender, SelectionChangedEventArgs e) { if (_adapter is not null && SizeBox.SelectedIndex >= 0) _settings.ThumbnailSize = (ThumbnailSizePreference)SizeBox.SelectedIndex; }
    private void UpdateSize(ThumbnailSizePreference preference) { SizeBox.SelectedIndex = (int)preference; if (_layout is not null) _layout.BaseItemWidth = preference.ToPixels(); }
    private void ScrollFirst() { if (_items.Count > 0) GalleryGrid.ScrollIntoView(_items[0]); }
    private void Search_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { SearchBox.Focus(FocusState.Keyboard); e.Handled = true; }
    private void First_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { ScrollFirst(); e.Handled = true; }
    private void Last_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { if (_items.Count > 0) GalleryGrid.ScrollIntoView(_items[^1]); e.Handled = true; }
    private void Browser_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 900;
        Grid.SetRow(SortControls, narrow ? 1 : 0); Grid.SetColumn(SortControls, narrow ? 0 : 1); Grid.SetColumnSpan(SortControls, narrow ? 2 : 1);
        Grid.SetRow(SearchActions, narrow ? 1 : 0); Grid.SetColumn(SearchActions, narrow ? 0 : 1); Grid.SetColumnSpan(SearchActions, narrow ? 2 : 1);
    }
    private void Container_Changing(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not GalleryEntry entry) return;
        if (args.InRecycleQueue) { _adapter?.Thumbnail(entry.Card, true); return; }
        args.RegisterUpdateCallback((_, phase) => { if (phase.Item is GalleryEntry item) _adapter?.Thumbnail(item.Card); });
        _layout?.EnsureLayoutOnFirstContent();
    }
    private void RequestVisible()
    {
        if (!_active || GalleryGrid.ItemsPanelRoot is not ItemsWrapGrid panel) return;
        for (var i = Math.Max(0, panel.FirstVisibleIndex); i <= panel.LastVisibleIndex && i < _items.Count; i++) _adapter?.Thumbnail(_items[i].Card);
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => _frame?.Navigate(typeof(SettingsPage), "filters");
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { CommandParameter: GalleryEntry entry }) UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Browser.OpenFolder", async () =>
        { var folder = Path.GetDirectoryName(entry.Card.FilePath); if (folder is not null) await Launcher.LaunchFolderPathAsync(folder); });
    }
    private void Drag_Starting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<GalleryEntry>().SelectMany(i => i.Members).Select(c => c.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.Data.SetDataProvider(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try { var files = new List<IStorageItem>(); foreach (var path in paths) { try { files.Add(await StorageFile.GetFileFromPathAsync(path)); } catch (Exception ex) { App.Services.GetRequiredService<IAppLogger>().LogError("Browser.Drag", ex, path); } } request.SetData(files); }
            finally { deferral.Complete(); }
        });
    }
}

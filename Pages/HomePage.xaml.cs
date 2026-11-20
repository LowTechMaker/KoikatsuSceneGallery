using KoikatsuSceneGallery.Controls;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

/// <summary>
/// The start page: a few shelves that answer "which scene do I play now"
/// without searching, filtering or choosing a platform first.
/// </summary>
public sealed partial class HomePage : Page
{
    private readonly HomeViewModel _viewModel = App.Services.GetRequiredService<HomeViewModel>();
    private readonly CardStateService _states = App.Services.GetRequiredService<CardStateService>();
    private readonly GalleryViewModel _gallery = App.Services.GetRequiredService<GalleryViewModel>();
    private readonly SettingsViewModel _settings = App.Services.GetRequiredService<SettingsViewModel>();
    private readonly CardInspector _inspector = new() { Width = UiMetrics.InspectorWidth, Margin = UiMetrics.InspectorMargin(UiMetrics.PaneGap) };
    private readonly CardLoupe _loupe = new() { Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 16) };
    private readonly List<CardShelf> _rows = [];
    private HomeShelf? _selectedShelf;
    private CardBase? _selected;
    private bool _wideEnoughForInspector = true;
    private bool _refreshQueued;

    /// <summary>Below this page width the pane would squeeze the shelves; it hides.</summary>
    private const double InspectorMinPageWidth = UiMetrics.InspectorMinHostWidth;

    public HomePage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        foreach (var shelf in _viewModel.Shelves)
        {
            var row = new CardShelf(shelf, () => Frame);
            row.CardInvoked += OpenInLoupe;
            row.CardSelected += Select;
            _rows.Add(row);
            row.FlagInvoked += ToggleFlag;
            if (shelf.Key == HomeViewModel.DealKey)
            {
                var again = new Button { Content = UiText.Get("Home_DealAgain") };
                AutomationProperties.SetAutomationId(again, "HomeDealAgain");
                again.Click += (_, _) => _viewModel.DealAgain();
                row.AddAction(again);
            }
            if (shelf.Key.StartsWith(HomeViewModel.ThemeKeyPrefix, StringComparison.Ordinal))
                AddThemeActions(row, shelf);
            Shelves.Children.Add(row);
        }
        InitDetails();
    }

    // ------------------------------------------------------------ select, inspect, open in place

    private void InitDetails()
    {
        Microsoft.UI.Xaml.Controls.Grid.SetRow(_inspector, 1);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(_inspector, 1);
        PageRoot.Children.Add(_inspector);
        _inspector.Frame = () => Frame;
        _inspector.OpenRequested += card =>
        {
            if (_selectedShelf is { } shelf) OpenInLoupe(shelf, card);
        };

        Microsoft.UI.Xaml.Controls.Grid.SetRow(_loupe, 1);
        PageRoot.Children.Add(_loupe);
        _loupe.TileBounds = TileBounds;
        _loupe.CurrentChanged += card => ShowSelection(card);
        // The loupe is a translucent card panel: the shelves behind it go while
        // it is open, and come back as it starts to close (so the tile can be
        // measured for the flight home).
        _loupe.Closing += () => ShowShelves(true);
        _loupe.Closed += card =>
        {
            ShowShelves(true);
            _inspector.ShowPreview = true;
            if (card is not null && TileOf(card) is { } tile)
                // Programmatic: the keyboard focus frame drew a white rectangle
                // round the tile after every close (live check, wave 16 round 3).
                tile.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        };
        // While the loupe is open its keys win wherever focus is.
        PageRoot.PreviewKeyDown += (_, e) =>
        {
            if (!_loupe.IsOpen || e.OriginalSource is TextBox or AutoSuggestBox) return;
            if (CardKeys.IsModifierDown()) return;
            if (_loupe.HandleKey(e.Key)) { e.Handled = true; return; }
            if (e.KeyStatus.WasKeyDown) return;
            if (CardKeys.Flag(e.Key) is { } flag) { e.Handled = true; _inspector.Toggle(flag); }
            else if (CardKeys.Rating(e.Key) is { } rating) { e.Handled = true; _inspector.RateFromKey(rating); }
        };

        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.InspectorVisible)) UpdateInspectorVisibility();
        };
        PageRoot.SizeChanged += (_, e) =>
        {
            var wide = e.NewSize.Width >= InspectorMinPageWidth;
            if (wide == _wideEnoughForInspector) return;
            _wideEnoughForInspector = wide;
            UpdateInspectorVisibility();
        };
        UpdateInspectorVisibility();
    }

    private void UpdateInspectorVisibility()
        => _inspector.Visibility = _settings.InspectorVisible && _wideEnoughForInspector
            ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private void Inspector_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs e)
    {
        _settings.InspectorVisible = !_settings.InspectorVisible;
        e.Handled = true;
    }

    private void Select(HomeShelf shelf, CardBase card)
    {
        _selectedShelf = shelf;
        ShowSelection(card);
    }

    /// <summary>Marks the card on every shelf that shows it and puts it in the details pane.</summary>
    private void ShowSelection(CardBase card)
    {
        _selected = card;
        foreach (var row in _rows) row.ShowSelected(card.FilePath);
        _inspector.Show([card]);
    }

    /// <summary>Opens a shelf's card large over the shelves, stepping through that shelf.</summary>
    private void OpenInLoupe(HomeShelf shelf, CardBase card)
    {
        var cards = shelf.Cards.ToArray();
        var index = Array.FindIndex(cards, c => ReferenceEquals(c, card));
        if (index < 0) return;
        _selectedShelf = shelf;
        ShowSelection(card);
        _inspector.ShowPreview = false;
        _loupe.Open(cards, index);
        ShowShelves(false);
    }

    private void ShowShelves(bool show)
    {
        ShelvesScroll.Opacity = show ? 1 : 0;
        ShelvesScroll.IsHitTestVisible = show;
    }

    private Microsoft.UI.Xaml.FrameworkElement? TileOf(CardBase card)
    {
        // The shelf the card was opened from first: the same card can be on several.
        foreach (var row in _rows.OrderBy(r => ReferenceEquals(r.Shelf, _selectedShelf) ? 0 : 1))
            if (row.PictureOf(card) is { } picture) return picture;
        return null;
    }

    private Windows.Foundation.Rect? TileBounds(CardBase card)
    {
        if (TileOf(card) is not { ActualWidth: > 0 } tile) return null;
        // Window coordinates (CardLoupe.TileBounds).
        var bounds = tile.TransformToVisual(null).TransformBounds(
            new Windows.Foundation.Rect(0, 0, tile.ActualWidth, tile.ActualHeight));
        var stage = _loupe.TransformToVisual(null).TransformBounds(
            new Windows.Foundation.Rect(0, 0, _loupe.ActualWidth, _loupe.ActualHeight));
        // Off the visible part of the page: just appear and disappear.
        if (bounds.Bottom < stage.Top || bounds.Top > stage.Bottom) return null;
        return bounds;
    }

    // A themed shelf: "see all" when today's theme is an author (their page);
    // the first one also carries the one button that rerolls the group.
    private void AddThemeActions(CardShelf row, HomeShelf shelf)
    {
        var seeAll = new Button { Content = UiText.Get("Home_ThemeSeeAll") };
        AutomationProperties.SetAutomationId(seeAll, $"HomeShelf_{shelf.Key}_SeeAll");
        seeAll.Click += (_, _) =>
        {
            if (shelf.SubjectKey is { } key && _viewModel.AuthorFor(key) is { } summary)
                Frame.Navigate(typeof(AuthorDetailPage), new AuthorDetailNavigationParameter(summary));
        };
        void Follow(HomeShelf themed)
            => seeAll.Visibility = themed is { Theme: ThemeKind.Author, SubjectKey: not null }
                ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        row.ThemeChanged += Follow;
        Follow(shelf);
        row.AddAction(seeAll);

        if (shelf.Key == HomeViewModel.ThemeKeyPrefix + "0")
        {
            var reroll = new Button { Content = UiText.Get("Home_ThemesReroll") };
            AutomationProperties.SetAutomationId(reroll, "HomeThemesReroll");
            reroll.Click += (_, _) => _viewModel.RerollThemes();
            row.AddAction(reroll);
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _gallery.CardsReloaded += QueueRefresh;
        _gallery.CardsReloaded += Reconcile;
        // The sidecars are read after the cards: tags (for the themes) come then.
        _viewModel.CardInfoChanged += QueueRefresh;
        // The rating filter (title bar, settings switch) and the ratings themselves.
        _gallery.RatingsChanged += QueueRefresh;
        _gallery.PropertyChanged += Gallery_PropertyChanged;
        // With a warm cache the whole library is on screen before the scan: the
        // page is usable from then on, and corrects itself when the scan is done.
        _gallery.CachedCardsShown += QueueRefresh;
        _states.StateChanged += State_Changed;
        // The shelves ask for thumbnails of cards the gallery may not have
        // shown, and leaving the gallery cancelled its thumbnail session.
        _gallery.ActivateThumbnailRequests();
        if (_gallery.Cards.Count == 0) _ = _gallery.LoadCardsCommand.ExecuteAsync(null);
        await _states.EnsureLoadedAsync();
        _viewModel.Refresh();
        // Queued, used and hidden are only known now: the pair is checked again.
        Duel.Refresh();
        Reconcile();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _loupe.Close(animate: false);
        _gallery.CardsReloaded -= QueueRefresh;
        _gallery.CardsReloaded -= Reconcile;
        _viewModel.CardInfoChanged -= QueueRefresh;
        _gallery.RatingsChanged -= QueueRefresh;
        _gallery.PropertyChanged -= Gallery_PropertyChanged;
        _gallery.CachedCardsShown -= QueueRefresh;
        _states.StateChanged -= State_Changed;
        base.OnNavigatedFrom(e);
    }

    // Finds states whose card was moved or renamed outside the app. Runs in
    // the background; whatever it re-attaches comes back through StateChanged,
    // which refreshes the shelves. Skipped while the scene library is still
    // loading: half-loaded, most states would look orphaned, and CardsReloaded
    // calls this again when the load is done.
    private void Reconcile()
    {
        if (_gallery.IsLoading) return;
        string[] paths = [.. App.Services.GetRequiredService<LibraryRegistry>().All
            .SelectMany(library => library.Cards)
            .Select(card => card.FilePath)];
        _states.ReconcileAsync(paths).Observe(
            App.Services.GetRequiredService<IAppLogger>(), "CardState.Reconcile");
    }

    // Raised off the UI thread, once per card: a batch of ten changes should
    // rebuild the shelves once.
    private void State_Changed(string path) => QueueRefresh();

    private void Gallery_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // The filter, and whether hidden cards count (the tiles' "N more").
        if (e.PropertyName is nameof(GalleryViewModel.RatingFilter) or nameof(GalleryViewModel.ShowHiddenCards)) QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _refreshQueued = false;
            _viewModel.Refresh();
            // A card queued or hidden elsewhere (or a reload) leaves the pair.
            Duel.Refresh();
        });
    }

    private async void ToggleFlag(CardBase card, CardFlag flag)
    {
        if (await _states.ToggleAsync(card.FilePath, flag) is null)
            AppToast.Show(UiText.Get("CardState_SaveFailed"), InfoBarSeverity.Error, key: "card-state");
    }
}

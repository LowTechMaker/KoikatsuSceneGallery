using KoikatsuSceneGallery.Controls;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

/// <summary>
/// The home page as a dashboard: the featured card and the library's numbers
/// on top, the shelves (queued, recent, liked, today's themes) as panels below.
/// </summary>
/// <remarks>
/// Redesigned on the user's request: the rows of wrapping thumbnails read
/// like a TV app, the details pane beside them took a third of the width for
/// one card, and the pair to choose between gave way to a single featured
/// card. Clicking any picture opens it in place (the loupe) among its panel's
/// cards; a panel's "see all" opens its list in the library's browser.
/// </remarks>
public sealed partial class HomePage : Page
{
    private readonly HomeViewModel _viewModel = App.Services.GetRequiredService<HomeViewModel>();
    private readonly CardStateService _states = App.Services.GetRequiredService<CardStateService>();
    private readonly GalleryViewModel _gallery = App.Services.GetRequiredService<GalleryViewModel>();
    private readonly CardLoupe _loupe = new() { Margin = new Thickness(0, 0, 0, 16) };
    private readonly HomeFeatured _featured;
    private readonly HomeStats _stats = new();
    private readonly List<HomePanel> _panels = [];
    /// <summary>The queued, recent and liked panels, which sit under the numbers when there is room.</summary>
    private readonly List<HomePanel> _sidePanels = [];
    private readonly StackPanel _sideColumn = new();
    private HomeShelf? _openShelf;
    private CardBase? _loupeCard;
    private bool _refreshQueued;

    public HomePage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        _featured = new HomeFeatured(_viewModel);
        _featured.CardInvoked += OpenInLoupe;
        _stats.NewRequested += cards => Frame.Navigate(typeof(ShelfPage), new ShelfPageParameter(UiText.Get("Home_NewThisWeek"), cards));
        TopBand.Children.Add(_featured);
        _sideColumn.Spacing = UiMetrics.HomePanelGap;
        _sideColumn.Children.Add(_stats);
        TopBand.Children.Add(_sideColumn);

        // Every shelf but the dealt hand, which is what the featured card steps through.
        foreach (var shelf in _viewModel.Shelves.Where(shelf => shelf.Key != HomeViewModel.DealKey))
        {
            var panel = new HomePanel(shelf, () => Frame);
            panel.CardInvoked += OpenInLoupe;
            panel.SeeAllRequested += SeeAll;
            // A panel hides itself when its shelf is empty: the rest close up.
            panel.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => LayOut());
            _panels.Add(panel);
            if (shelf.Key is HomeViewModel.QueuedKey or HomeViewModel.RecentKey or HomeViewModel.LikedKey) _sidePanels.Add(panel);
            PanelsGrid.Children.Add(panel);
        }
        Dashboard.SizeChanged += (_, _) => LayOut();
        InitLoupe();
    }

    // ------------------------------------------------------------ layout

    /// <summary>
    /// With room for both: the featured card on the left, and on the right the
    /// library's numbers with the queued, recent and liked panels under them
    /// (user request: they filled nothing there); today's themes below, as
    /// many per row as fit (at most three). Without the room, everything
    /// stacks and those three panels join the themes.
    /// </summary>
    private void LayOut()
    {
        var width = Dashboard.ActualWidth - Dashboard.Margin.Left - Dashboard.Margin.Right;
        if (width <= 0) return;
        var gap = UiMetrics.HomePanelGap;
        var sideBySide = width >= UiMetrics.HomeFeaturedMinWidth + gap + UiMetrics.HomeSideColumnWidth;
        foreach (var panel in _sidePanels)
        {
            Move(panel, sideBySide ? _sideColumn : PanelsGrid);
            // Beside the featured card they are one row each, so the top of the
            // page and the themes under it fit in the window (user report).
            panel.Compact = sideBySide;
        }

        var columns = (int)Math.Clamp(Math.Floor((width + gap) / (UiMetrics.HomePanelMinWidth + gap)), 1, UiMetrics.HomePanelColumns);
        PanelsGrid.ColumnSpacing = gap;
        PanelsGrid.RowSpacing = gap;
        PanelsGrid.ColumnDefinitions.Clear();
        PanelsGrid.RowDefinitions.Clear();
        for (var c = 0; c < columns; c++) PanelsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var shown = _panels.Where(panel => panel.Visibility == Visibility.Visible && panel.Parent == PanelsGrid).ToArray();
        for (var r = 0; r < (shown.Length + columns - 1) / columns; r++) PanelsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < shown.Length; i++)
        {
            Grid.SetRow(shown[i], i / columns);
            Grid.SetColumn(shown[i], i % columns);
        }

        TopBand.ColumnSpacing = gap;
        TopBand.RowSpacing = gap;
        TopBand.ColumnDefinitions.Clear();
        TopBand.RowDefinitions.Clear();
        TopBand.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (sideBySide) TopBand.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiMetrics.HomeSideColumnWidth) });
        else
        {
            TopBand.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TopBand.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        Grid.SetColumn(_sideColumn, sideBySide ? 1 : 0);
        Grid.SetRow(_sideColumn, sideBySide ? 0 : 1);
        // Side by side, the featured card is as tall as the column beside it
        // and keeps its picture in the middle; stacked, it is its own height.
        _featured.VerticalAlignment = sideBySide ? VerticalAlignment.Stretch : VerticalAlignment.Top;
    }

    /// <summary>Puts a panel in another container, once; a no-op when it is already there.</summary>
    private static void Move(FrameworkElement element, Panel target)
    {
        if (ReferenceEquals(element.Parent, target)) return;
        (element.Parent as Panel)?.Children.Remove(element);
        target.Children.Add(element);
    }

    // ------------------------------------------------------------ open in place (the loupe)

    private void InitLoupe()
    {
        Grid.SetRow(_loupe, 1);
        PageRoot.Children.Add(_loupe);
        _loupe.TileBounds = TileBounds;
        _loupe.CurrentChanged += card => _loupeCard = card;
        // The loupe is a translucent panel: the dashboard behind it goes while
        // it is open, and comes back as it starts to close (so the picture can
        // be measured for the flight home).
        _loupe.Closing += () => ShowDashboard(true);
        _loupe.Closed += card =>
        {
            ShowDashboard(true);
            if (card is not null && PictureOf(card) is Control tile)
                tile.Focus(FocusState.Programmatic);
        };
        // While the loupe is open its keys win wherever focus is: arrows, zoom,
        // and the card keys (queue, like, hide, rate), now that no details pane
        // takes them.
        PageRoot.PreviewKeyDown += (_, e) =>
        {
            if (!_loupe.IsOpen || e.OriginalSource is TextBox or AutoSuggestBox) return;
            if (CardKeys.IsModifierDown()) return;
            if (_loupe.HandleKey(e.Key)) { e.Handled = true; return; }
            if (e.KeyStatus.WasKeyDown || _loupeCard is not { } card) return;
            if (CardKeys.Flag(e.Key) is { } flag) { e.Handled = true; ToggleFlag(card, flag); }
            else if (CardKeys.Rating(e.Key) is { } rating && card is SceneCard scene)
            {
                e.Handled = true;
                _ = CardRatingActions.RateManyAsync([scene], rating);
            }
        };
    }

    private void OpenInLoupe(HomeShelf shelf, CardBase card)
    {
        var cards = shelf.Cards.ToArray();
        var index = Array.FindIndex(cards, c => ReferenceEquals(c, card));
        if (index < 0) return;
        _openShelf = shelf;
        _loupeCard = card;
        _loupe.Open(cards, index);
        ShowDashboard(false);
    }

    private void ShowDashboard(bool show)
    {
        ShelvesScroll.Opacity = show ? 1 : 0;
        ShelvesScroll.IsHitTestVisible = show;
    }

    private FrameworkElement? PictureOf(CardBase card)
    {
        // The panel the card was opened from first: one card can be on several.
        if (ReferenceEquals(_openShelf, _viewModel.Deal) && _featured.PictureOf(card) is { } featured) return featured;
        foreach (var panel in _panels.OrderBy(p => ReferenceEquals(p.Shelf, _openShelf) ? 0 : 1))
            if (panel.PictureOf(card) is { } picture) return picture;
        return _featured.PictureOf(card);
    }

    private Windows.Foundation.Rect? TileBounds(CardBase card)
    {
        if (PictureOf(card) is not { ActualWidth: > 0 } tile) return null;
        // Window coordinates (CardLoupe.TileBounds).
        var bounds = tile.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, tile.ActualWidth, tile.ActualHeight));
        var stage = _loupe.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, _loupe.ActualWidth, _loupe.ActualHeight));
        // Off the visible part of the page: just appear and disappear.
        if (bounds.Bottom < stage.Top || bounds.Top > stage.Bottom) return null;
        return bounds;
    }

    // ------------------------------------------------------------ see all

    private void SeeAll(HomeShelf shelf)
    {
        // Today's author has a page of their own; any other shelf opens its list.
        if (shelf is { Theme: ThemeKind.Author, SubjectKey: { } key } && _viewModel.AuthorFor(key) is { } summary)
        {
            Frame.Navigate(typeof(AuthorDetailPage), new AuthorDetailNavigationParameter(summary));
            return;
        }
        Frame.Navigate(typeof(ShelfPage), new ShelfPageParameter(shelf.Title, [.. shelf.Cards]));
    }

    /// <summary>
    /// New themes: today's theme panels go together, the new ones come in one
    /// after another (CardMotion), so the change reads as a new hand rather
    /// than pictures flickering in place.
    /// </summary>
    private void Reroll_Click(object sender, RoutedEventArgs e)
    {
        RerollButton.IsEnabled = false;
        var themes = ThemePanels();
        CardMotion.LeaveTogether(themes, () => DispatcherQueue.TryEnqueue(() =>
        {
            _viewModel.RerollThemes();
            // The panels rebuild from their shelves on the next tick; arrive after that.
            DispatcherQueue.TryEnqueue(() =>
            {
                var arriving = ThemePanels();
                CardMotion.EnterStaggered(arriving, rise: 12);
                // A panel whose new theme came out empty is hidden now: it still
                // carries the fade, so it is brought back to normal for next time.
                foreach (var gone in themes.Except(arriving)) CardMotion.Entrance(gone, 0);
                RerollButton.IsEnabled = true;
            });
        }));
    }

    private UIElement[] ThemePanels() => [.. _panels
        .Where(panel => panel.Shelf.Key.StartsWith(HomeViewModel.ThemeKeyPrefix, StringComparison.Ordinal)
            && panel.Visibility == Visibility.Visible)];

    /// <summary>
    /// The page arriving: the header, the featured card, the numbers and the
    /// panels beside them, then the rest, one after another (CardMotion).
    /// </summary>
    private void PlayEntrance()
    {
        List<UIElement> order = [Header, _featured, .. _sideColumn.Children];
        order.AddRange(_panels.Where(panel => panel.Parent == PanelsGrid && panel.Visibility == Visibility.Visible)
            .OrderBy(Grid.GetRow).ThenBy(Grid.GetColumn));
        CardMotion.EnterStaggered(order);
    }

    // ------------------------------------------------------------ keeping up

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        PlayEntrance();
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
        // The panels ask for thumbnails of cards the gallery may not have
        // shown, and leaving the gallery cancelled its thumbnail session.
        _gallery.ActivateThumbnailRequests();
        if (_gallery.Cards.Count == 0) _ = _gallery.LoadCardsCommand.ExecuteAsync(null);
        await _states.EnsureLoadedAsync();
        _viewModel.Refresh();
        _stats.Refresh();
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
    // the background; whatever it re-attaches comes back through StateChanged.
    // Skipped while the scene library is still loading: half-loaded, most
    // states would look orphaned, and CardsReloaded calls this again.
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
    // rebuild the panels once.
    private void State_Changed(string path) => QueueRefresh();

    private void Gallery_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
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
            _stats.Refresh();
        });
    }

    private async void ToggleFlag(CardBase card, CardFlag flag)
    {
        if (await _states.ToggleAsync(card.FilePath, flag) is null)
            AppToast.Show(UiText.Get("CardState_SaveFailed"), InfoBarSeverity.Error, key: "card-state");
    }
}

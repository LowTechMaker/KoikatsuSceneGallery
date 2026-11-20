using System.Collections.Specialized;
using System.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// One shelf of the home page as a panel of a dashboard: its title and count
/// with "see all", over a mosaic of one large picture and four small ones.
/// </summary>
/// <remarks>
/// The home page used to be rows of wrapping thumbnails, read top to bottom
/// like a TV app (user: "too much like a TV"). Panels sit two or three to a
/// row, so every shelf is in sight at once and nothing scrolls sideways; a
/// shelf's full list is one click away (<see cref="SeeAllRequested"/>).
/// </remarks>
internal sealed class HomePanel : UserControl
{
    private readonly TextBlock _title = new() { Style = (Style)Application.Current.Resources["SectionTitleTextBlockStyle"], TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _count = new()
    {
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly HyperlinkButton _seeAll = new() { Padding = new Thickness(6, 2, 6, 2) };
    private readonly Grid _mosaic = new() { ColumnSpacing = UiMetrics.HomeMosaicGap, RowSpacing = UiMetrics.HomeMosaicGap };
    private readonly TextBlock _empty = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(16),
    };
    private readonly Func<Frame?> _frame;
    private readonly List<(Button Button, Image Image, CardBase Card)> _tiles = [];

    public HomeShelf Shelf { get; }

    /// <summary>A picture was clicked: open it in place, among this shelf's cards.</summary>
    public event Action<HomeShelf, CardBase>? CardInvoked;

    /// <summary>The title or "see all" was clicked: the shelf's whole list.</summary>
    public event Action<HomeShelf>? SeeAllRequested;

    public HomePanel(HomeShelf shelf, Func<Frame?> frame)
    {
        Shelf = shelf;
        _frame = frame;
        AutomationProperties.SetAutomationId(this, $"HomePanel_{shelf.Key}");

        _seeAll.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = UiText.Get("Home_SeeAll") },
                new FontIcon { Glyph = Glyphs.Next, FontSize = 10 },
            },
        };
        AutomationProperties.SetAutomationId(_seeAll, $"HomePanel_{shelf.Key}_SeeAll");
        _seeAll.Click += (_, _) => SeeAllRequested?.Invoke(Shelf);

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_title);
        Grid.SetColumn(_count, 1);
        header.Children.Add(_count);
        Grid.SetColumn(_seeAll, 2);
        header.Children.Add(_seeAll);

        // Two columns of small pictures beside a large one twice their size:
        // every picture keeps the cards' own shape.
        _mosaic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        _mosaic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _mosaic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _mosaic.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _mosaic.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _mosaic.SizeChanged += (_, e) =>
        {
            // Small pictures at the cards' ratio: two rows of them make the height.
            var small = (e.NewSize.Width - 2 * UiMetrics.HomeMosaicGap) / 4;
            var height = 2 * small * UiMetrics.HolderCardAspect + UiMetrics.HomeMosaicGap;
            if (Math.Abs(_mosaic.Height - height) > 0.5) _mosaic.Height = height;
        };

        var body = new Grid();
        body.Children.Add(_mosaic);
        body.Children.Add(_empty);

        var layout = new StackPanel { Spacing = 8 };
        layout.Children.Add(header);
        layout.Children.Add(body);

        Content = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = (CornerRadius)Application.Current.Resources["OverlayCornerRadius"],
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = layout,
        };

        shelf.Cards.CollectionChanged += Cards_Changed;
        shelf.TitleChanged += Rebuild;
        // Built again on the way back, so the cards are watched again.
        Unloaded += (_, _) => { Unwatch(); _tiles.Clear(); _mosaic.Children.Clear(); };
        Loaded += (_, _) => Rebuild();
        Rebuild();
    }

    /// <summary>The picture of a card on this panel, for the loupe's flight; null when it is not shown.</summary>
    public FrameworkElement? PictureOf(CardBase card)
        => _tiles.FirstOrDefault(tile => ReferenceEquals(tile.Card, card)).Image;

    private void Cards_Changed(object? sender, NotifyCollectionChangedEventArgs e)
        => DispatcherQueue.TryEnqueue(Rebuild);

    private void Rebuild()
    {
        _title.Text = Shelf.Title;
        var total = Shelf.Cards.Count;
        _count.Text = total > 0 ? total.ToString("N0") : "";
        _seeAll.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
        // A shelf with nothing in it and nothing to say about that is not shown at all.
        Visibility = total == 0 && string.IsNullOrEmpty(Shelf.EmptyText) ? Visibility.Collapsed : Visibility.Visible;
        _empty.Text = Shelf.EmptyText;
        _empty.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        _mosaic.Opacity = total == 0 ? 0 : 1;

        var shown = Shelf.Cards.Take(UiMetrics.HomeMosaicCards).ToArray();
        if (shown.Length == _tiles.Count && shown.Select((card, i) => ReferenceEquals(card, _tiles[i].Card)).All(same => same)) return;

        Unwatch();
        _tiles.Clear();
        _mosaic.Children.Clear();
        for (var i = 0; i < shown.Length; i++)
        {
            var card = shown[i];
            var image = new Image { Stretch = Stretch.UniformToFill, Source = LibraryBrowser.Thumbnail(card.ThumbnailUri) };
            var face = new Border
            {
                CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                Child = image,
            };
            var button = new Button { Style = (Style)Application.Current.Resources["PlainTileButtonStyle"], Content = face };
            AutomationProperties.SetName(button, card.FileName);
            ToolTipService.SetToolTip(button, card.FileName);
            button.Click += (_, _) => CardInvoked?.Invoke(Shelf, card);
            // Dragged out to the game like any tile, and the same card menu.
            ButtonDragOut.Enable(button, () => card.FilePath, "Home.Drag");
            var menu = new MenuFlyout();
            menu.Opening += (_, _) => CardMenu.Fill(menu, [card], () => CardInvoked?.Invoke(Shelf, card), _frame());
            button.ContextFlyout = menu;

            // The first card large on the left, the next four in a square of two by two.
            if (i == 0) { Grid.SetRowSpan(button, 2); }
            else
            {
                Grid.SetColumn(button, 1 + (i - 1) % 2);
                Grid.SetRow(button, (i - 1) / 2);
            }
            _mosaic.Children.Add(button);
            _tiles.Add((button, image, card));
            card.PropertyChanged += Card_Changed;
            RequestThumbnail(card);
        }
    }

    private void Unwatch()
    {
        foreach (var tile in _tiles) tile.Card.PropertyChanged -= Card_Changed;
    }

    private void Card_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CardBase.ThumbnailUri) || sender is not CardBase card) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var tile in _tiles.Where(tile => ReferenceEquals(tile.Card, card)))
                tile.Image.Source = LibraryBrowser.Thumbnail(card.ThumbnailUri);
        });
    }

    // As the shelves did: leaving the gallery cancels its thumbnail session, so
    // it is opened again, and visible priority keeps these ahead of prefetching.
    private static void RequestThumbnail(CardBase card)
    {
        if (card is not SceneCard scene) return;
        var scenes = App.Services.GetRequiredService<GalleryViewModel>();
        scenes.ActivateThumbnailRequests();
        scenes.RequestThumbnail(scene, ThumbnailWorkPriority.Visible);
    }
}

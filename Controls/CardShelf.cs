using System.Collections.Specialized;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// One row of the home page: a heading with a count, then cards side by side.
/// </summary>
/// <remarks>
/// Skeleton. It shows the cards and raises the events the page needs; the
/// tiles are plain, the row is not virtualized and nothing appears on hover
/// yet. The thumbnail handling follows <see cref="Filmstrip"/>: a shelf shows
/// cards the gallery may never have scrolled to, so each tile asks for its own
/// thumbnail at visible priority after re-activating the gallery's session.
/// </remarks>
internal sealed class CardShelf : Grid
{
    private const double TileHeight = 135;

    private readonly HomeShelf _shelf;
    private readonly TextBlock _count = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _panel = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Enabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollMode = ScrollMode.Disabled,
    };
    private readonly TextBlock _empty = new()
    {
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Dictionary<string, Tile> _tiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The user opened a card.</summary>
    public event Action<HomeShelf, CardBase>? CardInvoked;

    /// <summary>The user asked to set or clear a flag on a card from the shelf.</summary>
#pragma warning disable CS0067 // raised once the tiles have their hover actions
    public event Action<CardBase, CardFlag>? FlagInvoked;
#pragma warning restore CS0067

    public CardShelf(HomeShelf shelf)
    {
        _shelf = shelf;
        RowSpacing = 8;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AutomationProperties.SetAutomationId(this, $"HomeShelf_{shelf.Key}");
        AutomationProperties.SetName(this, shelf.Title);

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        heading.Children.Add(new TextBlock
        {
            Text = shelf.Title,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        });
        heading.Children.Add(_count);
        var header = new Grid();
        header.Children.Add(heading);
        _actions.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(_actions);
        Children.Add(header);

        _scroll.Content = _panel;
        SetRow(_scroll, 1);
        Children.Add(_scroll);
        _empty.Text = shelf.EmptyText;
        SetRow(_empty, 1);
        Children.Add(_empty);

        // A wheel over the row scrolls the page, not the row: the page is the
        // thing the user is moving through, and the rows scroll by drag or bar.
        shelf.Cards.CollectionChanged += Cards_Changed;
        Unloaded += (_, _) => shelf.Cards.CollectionChanged -= Cards_Changed;
        Loaded += (_, _) =>
        {
            shelf.Cards.CollectionChanged -= Cards_Changed;
            shelf.Cards.CollectionChanged += Cards_Changed;
            Rebuild();
        };
        Rebuild();
    }

    /// <summary>Buttons shown at the right of the heading, such as "deal again".</summary>
    public void AddAction(Button button) => _actions.Children.Add(button);

    private void Cards_Changed(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        var cards = _shelf.Cards.ToArray();
        _count.Text = cards.Length == 0 ? "" : cards.Length.ToString();
        // A shelf with nothing to say disappears; one with a hint keeps its
        // heading so the hint has something to belong to.
        Visibility = cards.Length == 0 && string.IsNullOrEmpty(_shelf.EmptyText)
            ? Visibility.Collapsed : Visibility.Visible;
        _scroll.Visibility = cards.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _empty.Visibility = cards.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tiles = new Tile[cards.Length];
        for (var i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            if (!_tiles.TryGetValue(card.FilePath, out var tile) || !ReferenceEquals(tile.Card, card))
                _tiles[card.FilePath] = tile = new Tile(card, opened => CardInvoked?.Invoke(_shelf, opened));
            kept.Add(card.FilePath);
            tiles[i] = tile;
        }
        foreach (var stale in _tiles.Keys.Where(path => !kept.Contains(path)).ToArray())
            _tiles.Remove(stale);

        // Re-adding a tile unloads and reloads it, which drops its thumbnail.
        if (_panel.Children.SequenceEqual(tiles)) return;
        _panel.Children.Clear();
        foreach (var tile in tiles) _panel.Children.Add(tile);
    }

    private sealed class Tile : Button
    {
        private readonly Image _image = new() { Stretch = Stretch.UniformToFill };

        public CardBase Card { get; }

        public Tile(CardBase card, Action<CardBase> open)
        {
            Card = card;
            var ratio = card.Width > 0 && card.Height > 0 ? Math.Clamp((double)card.Width / card.Height, 0.5, 2.0) : 16.0 / 9;
            Width = Math.Round(TileHeight * ratio);
            Height = TileHeight;
            Padding = new Thickness(0);
            BorderThickness = new Thickness(0);
            // Without this a tile whose thumbnail has not arrived shrinks to nothing.
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            Background = new SolidColorBrush(Colors.Transparent);
            CornerRadius = new CornerRadius(6);
            AutomationProperties.SetName(this, card.FileName);
            ToolTipService.SetToolTip(this, card.FileName);
            Content = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["ControlStrongFillColorDefaultBrush"],
                Child = _image,
            };
            Click += (_, _) => open(Card);

            Loaded += (_, _) =>
            {
                Card.PropertyChanged -= Card_PropertyChanged;
                Card.PropertyChanged += Card_PropertyChanged;
                if (!LoadThumbnail()) RequestThumbnail(Card);
            };
            Unloaded += (_, _) =>
            {
                if (VisualTreeHelper.GetParent(this) is not null) return;
                Card.PropertyChanged -= Card_PropertyChanged;
                if (Card.ThumbnailUri is null)
                    App.Services.GetRequiredService<LibraryRegistry>().ForCard(Card).Thumbnail(Card, release: true);
            };
        }

        private static void RequestThumbnail(CardBase card)
        {
            if (card is not SceneCard scene) return;
            var scenes = App.Services.GetRequiredService<GalleryViewModel>();
            scenes.ActivateThumbnailRequests();
            scenes.RequestThumbnail(scene, ThumbnailWorkPriority.Visible);
        }

        private void Card_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CardBase.ThumbnailUri))
                DispatcherQueue.TryEnqueue(() => LoadThumbnail());
        }

        private bool LoadThumbnail()
        {
            if (Card.ThumbnailUri is not { } uri) return false;
            if (_image.Source is BitmapImage { UriSource: { } shown } && shown == uri) return true;
            _image.Source = new BitmapImage { DecodePixelHeight = (int)(TileHeight * 2), UriSource = uri };
            return true;
        }
    }
}

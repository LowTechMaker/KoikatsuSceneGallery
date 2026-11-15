using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The strip of neighbouring cards under a detail page's image. Shows a window
/// of the browse list around the current card; when the current card sits in
/// a folder or post with others, the cards outside it are darkened so the
/// folder's edges are visible.
/// </summary>
internal sealed class Filmstrip : Grid
{
    private const int Radius = 12;
    private const double TileHeight = 56;

    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollMode = ScrollMode.Enabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollMode = ScrollMode.Disabled,
    };
    private readonly StackPanel _panel = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly Dictionary<string, Tile> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Path, string? Provider, string? Author), string> _keys = [];
    private readonly Action<CardBase> _open;
    private Tile? _current;

    public Filmstrip(Action<CardBase> open)
    {
        _open = open;
        Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetName(this, UiText.Get("Filmstrip_Name"));
        AutomationProperties.SetAutomationId(this, "DetailFilmstrip");
        _scroll.Content = _panel;
        Children.Add(_scroll);
        // A wheel over the strip scrolls it sideways instead of doing nothing.
        _scroll.PointerWheelChanged += (_, e) =>
        {
            var delta = e.GetCurrentPoint(_scroll).Properties.MouseWheelDelta;
            _scroll.ChangeView(_scroll.HorizontalOffset - delta, null, null, true);
            e.Handled = true;
        };
        SizeChanged += (_, _) => CenterCurrent();
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Shows <paramref name="cards"/> around <paramref name="index"/>; a negative index hides the strip.</summary>
    public void Show(IReadOnlyList<CardBase> cards, int index)
    {
        var (start, count) = FilmstripPolicy.Window(cards.Count, index, Radius);
        if (count < 2)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        var window = new CardBase[count];
        var keys = new string[count];
        for (var i = 0; i < count; i++)
        {
            window[i] = cards[start + i];
            keys[i] = GroupKey(window[i]);
        }
        var dimmed = FilmstripPolicy.Dimmed(keys, index - start);

        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tiles = new Tile[count];
        for (var i = 0; i < count; i++)
        {
            var card = window[i];
            if (!_tiles.TryGetValue(card.FilePath, out var tile) || !ReferenceEquals(tile.Card, card))
                _tiles[card.FilePath] = tile = new Tile(card, _open);
            tile.SetState(i == index - start, dimmed[i]);
            kept.Add(card.FilePath);
            tiles[i] = tile;
        }
        foreach (var stale in _tiles.Keys.Where(path => !kept.Contains(path)).ToArray())
            _tiles.Remove(stale);

        // Only touch the panel when the run of cards changed: re-adding a tile
        // unloads and reloads it, which would drop its thumbnail request.
        if (!_panel.Children.SequenceEqual(tiles))
        {
            _panel.Children.Clear();
            foreach (var tile in tiles) _panel.Children.Add(tile);
        }

        _current = (Tile)_panel.Children[index - start];
        Visibility = Visibility.Visible;
        DispatcherQueue.TryEnqueue(CenterCurrent);
    }

    private void CenterCurrent()
    {
        if (_current is null || Visibility != Visibility.Visible || _scroll.ViewportWidth <= 0) return;
        _panel.UpdateLayout();
        var left = _current.TransformToVisual(_panel).TransformPoint(default).X;
        var target = left + _current.ActualWidth / 2 - _scroll.ViewportWidth / 2;
        _scroll.ChangeView(Math.Max(0, target), null, null, true);
    }

    private string GroupKey(CardBase card)
    {
        var author = (card as IAuthorOwner)?.Author;
        var cacheKey = (card.FilePath, author?.Key.ProviderId, author?.Key.Id);
        if (_keys.TryGetValue(cacheKey, out var key)) return key;
        key = CardGroupKey.For(card,
            App.Services.GetRequiredService<PluginService>(),
            App.Services.GetRequiredService<IAppLogger>());
        return _keys[cacheKey] = key;
    }

    private sealed class Tile : Button
    {
        // A fixed dark veil rather than opacity: the strip should read darker
        // on both themes, and a faded tile on a dark page reads lighter.
        private static readonly SolidColorBrush Veil = new(ColorHelper.FromArgb(0x99, 0, 0, 0));
        private readonly Border _frame;
        private readonly Border _veil;

        public CardBase Card { get; }

        public Tile(CardBase card, Action<CardBase> open)
        {
            Card = card;
            var ratio = card.Width > 0 && card.Height > 0 ? Math.Clamp((double)card.Width / card.Height, 0.5, 2.0) : 1.0;
            Width = Math.Round(TileHeight * ratio);
            Height = TileHeight;
            Padding = new Thickness(0);
            BorderThickness = new Thickness(0);
            // Without this the frame takes the image's size, and a tile whose
            // thumbnail has not arrived yet shrinks to nothing.
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            Background = new SolidColorBrush(Colors.Transparent);
            CornerRadius = new CornerRadius(6);
            AutomationProperties.SetName(this, card.FileName);
            ToolTipService.SetToolTip(this, card.FileName);

            _image = new Image { Stretch = Stretch.UniformToFill };
            _veil = new Border { Background = Veil, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            _frame = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                Background = (Brush)Application.Current.Resources["ControlStrongFillColorDefaultBrush"],
                Child = new Grid { Children = { _image, _veil } },
            };
            Content = _frame;
            Click += (_, _) => open(Card);

            // Thumbnails are made on demand, for what the gallery has shown. A
            // card the strip shows first asks for its own, and picks it up when
            // it is ready. Subscribed only while on screen, so a dropped tile
            // is not kept alive by its card.
            Loaded += (_, _) =>
            {
                Card.PropertyChanged -= Card_PropertyChanged;
                Card.PropertyChanged += Card_PropertyChanged;
                if (!LoadThumbnail()) RequestThumbnail(Card);
            };
            Unloaded += (_, _) =>
            {
                // A tile moved within the strip can be unloaded after it has
                // already been loaded again; it is still on screen then.
                if (VisualTreeHelper.GetParent(this) is not null) return;
                Card.PropertyChanged -= Card_PropertyChanged;
                if (Card.ThumbnailUri is null) App.Services.GetRequiredService<LibraryRegistry>().ForCard(Card).Thumbnail(Card, release: true);
            };
        }

        // As the author page does: leaving the gallery cancels its thumbnail
        // session, which refuses every request until it is activated again,
        // and the visible priority keeps these ahead of any prefetch work.
        private static void RequestThumbnail(CardBase card)
        {
            switch (card)
            {
                case SceneCard scene:
                    var scenes = App.Services.GetRequiredService<ViewModels.GalleryViewModel>();
                    scenes.ActivateThumbnailRequests();
                    scenes.RequestThumbnail(scene, ThumbnailWorkPriority.Visible);
                    break;
                case CharacterCard character:
                    var characters = App.Services.GetRequiredService<ViewModels.CharacterGalleryViewModel>();
                    characters.ActivateThumbnailRequests();
                    characters.RequestThumbnail(character, ThumbnailWorkPriority.Visible);
                    break;
                case CoordinateCard coordinate:
                    var coordinates = App.Services.GetRequiredService<ViewModels.CoordinateGalleryViewModel>();
                    coordinates.ActivateThumbnailRequests();
                    coordinates.RequestThumbnail(coordinate, ThumbnailWorkPriority.Visible);
                    break;
            }
        }

        private readonly Image _image;

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

        public void SetState(bool current, bool dimmed)
        {
            _frame.BorderBrush = current
                ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : new SolidColorBrush(Colors.Transparent);
            _veil.Visibility = dimmed && !current ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetItemStatus(this, current ? UiText.Get("Filmstrip_Current") : "");
        }
    }
}

using System.Collections.Specialized;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// One row of the home page: a heading with a count, then cards side by side.
/// Each tile opens its card on click, can be dragged out to the game, and on
/// hover or keyboard focus shows the queue, like and hide buttons.
/// </summary>
/// <remarks>
/// The row is a plain panel, not a virtualizing list: a shelf holds at most
/// <see cref="HomeViewModel.ShelfLimit"/> cards, and a recycling list would
/// hand one tile several cards over its life, which the thumbnail lifecycle
/// below is not built for. What is expensive (the thumbnail) is still only
/// asked for when the tile comes near the screen (<see cref="Tile"/>).
///
/// The thumbnail handling follows <see cref="Filmstrip"/>: a shelf shows cards
/// the gallery may never have scrolled to, so each tile asks for its own
/// thumbnail at visible priority after re-activating the gallery's session.
/// </remarks>
internal sealed class CardShelf : Grid
{
    private const double TileHeight = 135;

    /// <summary>A Segoe Fluent Icons glyph by its code point.</summary>
    private static string Glyph(int codePoint) => char.ConvertFromUtf32(codePoint);

    private readonly HomeShelf _shelf;
    private readonly CardStateService _states = App.Services.GetRequiredService<CardStateService>();
    private readonly TextBlock _count = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    // The bottom margin is room for the horizontal scroll bar, which WinUI draws
    // over the content: without it the bar runs through the caption's last line.
    private readonly StackPanel _panel = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Margin = new Thickness(0, 0, 0, 16),
    };
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
    private readonly HomeViewModel? _home = App.Services.GetService<HomeViewModel>();
    private readonly TextBlock _title = new() { Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] };
    private bool _rebuildQueued;
    private bool _subscribed;

    /// <summary>The user opened a card.</summary>
    public event Action<HomeShelf, CardBase>? CardInvoked;

    /// <summary>The user asked to set or clear a flag on a card from the shelf.</summary>
    public event Action<CardBase, CardFlag>? FlagInvoked;

    public CardShelf(HomeShelf shelf)
    {
        _shelf = shelf;
        RowSpacing = 8;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AutomationProperties.SetAutomationId(this, $"HomeShelf_{shelf.Key}");
        AutomationProperties.SetName(this, shelf.Title);

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _title.Text = shelf.Title;
        heading.Children.Add(_title);
        heading.Children.Add(_count);
        var header = new Grid();
        header.Children.Add(heading);
        _actions.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(_actions);
        Children.Add(header);

        AutomationProperties.SetAutomationId(_scroll, $"HomeShelf_{shelf.Key}_Row");
        _scroll.Content = _panel;
        // handledEventsToo: a tile's button may mark the wheel handled before it gets here.
        _scroll.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnWheel), handledEventsToo: true);
        _panel.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnWheel), handledEventsToo: true);
        SetRow(_scroll, 1);
        Children.Add(_scroll);
        _empty.Text = shelf.EmptyText;
        SetRow(_empty, 1);
        Children.Add(_empty);

        // Subscribed only while on screen, so a page that is gone is not kept
        // alive by the shelf's collection or the app-wide state service.
        Loaded += (_, _) =>
        {
            Subscribe();
            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            if (VisualTreeHelper.GetParent(this) is not null) return;
            Unsubscribe();
        };
        Subscribe();
        Rebuild();
    }

    /// <summary>Buttons shown at the right of the heading, such as "deal again".</summary>
    public void AddAction(Button button) => _actions.Children.Add(button);

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        _shelf.Cards.CollectionChanged += Cards_Changed;
        _shelf.TitleChanged += Title_Changed;
        _states.StateChanged += State_Changed;
        if (_home is not null) _home.CardInfoChanged += Info_Changed;
        Title_Changed();
    }

    /// <summary>Raised when the shelf's actions should follow a new theme (a "see all" that only some themes have).</summary>
    public event Action<HomeShelf>? ThemeChanged;

    // A themed shelf gets another title each day or reroll; on the UI thread (Refresh).
    private void Title_Changed()
    {
        _title.Text = _shelf.Title;
        AutomationProperties.SetName(this, _shelf.Title);
        ThemeChanged?.Invoke(_shelf);
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        _shelf.Cards.CollectionChanged -= Cards_Changed;
        _shelf.TitleChanged -= Title_Changed;
        _states.StateChanged -= State_Changed;
        if (_home is not null) _home.CardInfoChanged -= Info_Changed;
    }

    // Titles arrive after the sidecars were read, off the UI thread.
    private void Info_Changed()
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (_home is null) return;
            foreach (var tile in _tiles.Values) tile.UpdateText(_home.Describe(tile.Card));
        });

    // The view model replaces a shelf by clearing it and adding each card, one
    // event apiece; rebuilding once after the burst keeps the tiles in place
    // instead of tearing the row down thirty times.
    private void Cards_Changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        });
    }

    // Raised on whatever thread wrote the file, with the full path.
    private void State_Changed(string path)
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (_tiles.TryGetValue(path, out var tile)) tile.UpdateState();
        });

    private double _wheelTarget;
    private long _wheelTick;

    /// <summary>
    /// The mouse wheel scrolls the page, not the row. A row that only scrolls
    /// sideways would otherwise take the wheel for itself, so moving down the
    /// home page stopped at every shelf the pointer crossed. Shift+wheel and a
    /// tilt wheel still move the row; touch and touchpad panning never come
    /// through here.
    /// </summary>
    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var wheel = e.GetCurrentPoint(this).Properties;
        if (wheel.IsHorizontalMouseWheel
            || e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift)
            || e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
            return;

        // Reached through the panel first and the row's own viewer second:
        // the first to see it handles it, which also keeps the row's viewer
        // (a class handler that skips handled events) from scrolling sideways.
        if (e.Handled && ReferenceEquals(sender, _scroll)) return;

        ScrollViewer? page = null;
        for (var parent = VisualTreeHelper.GetParent(_scroll); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollViewer viewer) { page = viewer; break; }
        }
        if (page is null) return;

        // Several notches in a row: continue from where the last one was
        // headed, not from where the animation has got to so far.
        var now = Environment.TickCount64;
        if (now - _wheelTick > 300) _wheelTarget = page.VerticalOffset;
        _wheelTick = now;
        _wheelTarget = Math.Clamp(_wheelTarget - wheel.MouseWheelDelta, 0, page.ScrollableHeight);
        page.ChangeView(null, _wheelTarget, null);
        e.Handled = true;
    }

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
            var key = Path.GetFullPath(card.FilePath);
            if (!_tiles.TryGetValue(key, out var tile) || !ReferenceEquals(tile.Card, card))
            {
                _tiles[key] = tile = new Tile(card, _states,
                    opened => CardInvoked?.Invoke(_shelf, opened),
                    (flagged, flag) => FlagInvoked?.Invoke(flagged, flag));
            }
            tile.SetAutomationIds($"HomeShelf_{_shelf.Key}_Tile{i}");
            tile.UpdateState();
            if (_home is not null) tile.UpdateText(_home.Describe(card));
            kept.Add(key);
            tiles[i] = tile;
        }
        foreach (var stale in _tiles.Keys.Where(path => !kept.Contains(path)).ToArray())
            _tiles.Remove(stale);

        // Re-adding a tile unloads and reloads it, which drops its thumbnail.
        if (_panel.Children.SequenceEqual(tiles)) return;
        _panel.Children.Clear();
        foreach (var tile in tiles) _panel.Children.Add(tile);
    }

    /// <summary>One card: its picture, its name, and the three flag buttons over the picture.</summary>
    private sealed class Tile : Grid
    {
        private readonly CardStateService _states;
        private readonly Button _open = new()
        {
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            // Without this a tile whose thumbnail has not arrived shrinks to nothing.
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            CornerRadius = new CornerRadius(6),
        };
        private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
        private readonly StackPanel _flags = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
        };
        private readonly FlagButton _queue;
        private readonly FlagButton _like;
        private readonly FlagButton _hide;
        private readonly Border _used = new()
        {
            Style = (Style)Application.Current.Resources["MediaOverlayBadgeStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = new FontIcon
            {
                Glyph = Glyph(0xE73E), // CheckMark
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["MediaOverlayForegroundBrush"],
            },
        };
        private readonly HomeCardCaption _caption = new();
        private bool _pointerOver;
        private bool _thumbnailRequested;

        public CardBase Card { get; }

        /// <summary>Caption, tooltip and accessible name from what the home page knows about the card.</summary>
        public void UpdateText(HomeCardInfo info) => _caption.Show(info, _open);

        public Tile(CardBase card, CardStateService states, Action<CardBase> open, Action<CardBase, CardFlag> flag)
        {
            Card = card;
            _states = states;
            var ratio = card.Width > 0 && card.Height > 0 ? Math.Clamp((double)card.Width / card.Height, 0.5, 2.0) : 16.0 / 9;
            Width = Math.Round(TileHeight * ratio);
            RowSpacing = 4;
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(TileHeight) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Until the shelf describes it (UpdateText): the file name, as before.
            AutomationProperties.SetName(_open, card.FileName);
            ToolTipService.SetToolTip(_open, card.FileName);
            _open.Content = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["ControlStrongFillColorDefaultBrush"],
                Child = _image,
            };
            _open.Click += (_, _) => open(Card);
            // Dragged out like a gallery tile, through the same funnel, so it
            // is recorded as used and in record mode alike. CanDrag alone does
            // nothing on a button; see ButtonDragOut.
            ButtonDragOut.Enable(_open, () => Card.FilePath, "Home.Drag");

            _queue = new FlagButton(CardFlag.Queued, Glyph(0xE718), Glyph(0xE841), "CardState_Queue", "CardState_Unqueue"); // Pin / PinFill
            _like = new FlagButton(CardFlag.Liked, Glyph(0xEB51), Glyph(0xEB52), "CardState_Like", "CardState_Unlike"); // Heart / HeartFill
            _hide = new FlagButton(CardFlag.Hidden, Glyph(0xE894), Glyph(0xEB90), "CardState_Hide", "CardState_Unhide"); // Clear / StatusErrorFull
            foreach (var button in new[] { _queue, _like, _hide })
            {
                var which = button.Flag;
                button.Click += (_, _) => flag(Card, which);
                _flags.Children.Add(button);
            }

            // The tick is named for UI Automation, but a Border has no automation
            // peer and an icon is easy to skip; UpdateState also puts the same
            // word in the open button's item status, which a screen reader
            // announces with the card's name.
            var usedText = UiText.Get("CardState_Used");
            AutomationProperties.SetName(_used, usedText);
            AutomationProperties.SetName(_used.Child, usedText);

            // The flag buttons come after the open button in the tree, so Tab
            // from a focused tile walks into them once focus has shown them.
            var picture = new Grid { Children = { _open, _used, _flags } };
            Children.Add(picture);

            // What the card is and who made it, not what the file is called.
            SetRow(_caption, 1);
            Children.Add(_caption);

            PointerEntered += (_, _) => { _pointerOver = true; ShowFlags(true); };
            PointerExited += (_, _) => { _pointerOver = false; ShowFlags(HasFocusWithin()); };
            PointerCanceled += (_, _) => { _pointerOver = false; ShowFlags(HasFocusWithin()); };
            GotFocus += (_, _) => ShowFlags(true);
            // While LostFocus runs the new focus is not settled yet; look once it is.
            LostFocus += (_, _) => DispatcherQueue.TryEnqueue(() => ShowFlags(_pointerOver || HasFocusWithin()));

            // Thumbnails are made on demand. A tile asks for its own only once it
            // comes within a screen of view, so a page of four shelves does not
            // queue a hundred visible-priority requests for cards off to the right.
            // Subscribed only while on screen, so a dropped tile is not kept
            // alive by its card.
            Loaded += (_, _) =>
            {
                Card.PropertyChanged -= Card_PropertyChanged;
                Card.PropertyChanged += Card_PropertyChanged;
                LoadThumbnail();
            };
            EffectiveViewportChanged += Tile_EffectiveViewportChanged;
            Unloaded += (_, _) =>
            {
                // A tile moved within the row can be unloaded after it has
                // already been loaded again; it is still on screen then.
                if (VisualTreeHelper.GetParent(this) is not null) return;
                Card.PropertyChanged -= Card_PropertyChanged;
                if (_thumbnailRequested && Card.ThumbnailUri is null)
                    App.Services.GetRequiredService<LibraryRegistry>().ForCard(Card).Thumbnail(Card, release: true);
                _thumbnailRequested = false;
            };
        }

        public void SetAutomationIds(string id)
        {
            AutomationProperties.SetAutomationId(_open, id);
            AutomationProperties.SetAutomationId(_queue, $"{id}_Queue");
            AutomationProperties.SetAutomationId(_like, $"{id}_Like");
            AutomationProperties.SetAutomationId(_hide, $"{id}_Hide");
            AutomationProperties.SetAutomationId(_used.Child, $"{id}_Used");
        }

        /// <summary>Re-reads the card's state: the flag glyphs and the used tick.</summary>
        public void UpdateState()
        {
            var path = Card.FilePath;
            _queue.Show(_states.Has(path, CardFlag.Queued));
            _like.Show(_states.Has(path, CardFlag.Liked));
            _hide.Show(_states.Has(path, CardFlag.Hidden));
            var used = _states.Get(path)?.LastUsedAt is not null;
            _used.Visibility = used ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetItemStatus(_open, used ? UiText.Get("CardState_Used") : "");
        }

        private void ShowFlags(bool show) => _flags.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        private bool HasFocusWithin()
        {
            if (XamlRoot is null) return false;
            for (var element = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                 element is not null;
                 element = VisualTreeHelper.GetParent(element))
            {
                if (ReferenceEquals(element, this)) return true;
            }
            return false;
        }

        private void Tile_EffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs e)
        {
            if (_thumbnailRequested || Card.ThumbnailUri is not null) return;
            var view = e.EffectiveViewport;
            if (view.Width <= 0 || view.Height <= 0) return;
            // One viewport of lead on each side, so a tile is ready by the time
            // the row has scrolled to it.
            var near = view.X - view.Width < ActualWidth && view.X + 2 * view.Width > 0
                && view.Y - view.Height < ActualHeight && view.Y + 2 * view.Height > 0;
            if (!near) return;
            _thumbnailRequested = true;
            RequestThumbnail(Card);
        }

        // As the author page does: leaving the gallery cancels its thumbnail
        // session, which refuses every request until it is activated again,
        // and the visible priority keeps these ahead of any prefetch work.
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

    /// <summary>A small round button over the picture whose glyph is filled while its flag is set.</summary>
    private sealed class FlagButton : Button
    {
        private readonly FontIcon _icon = new()
        {
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["MediaOverlayForegroundBrush"],
        };
        private readonly string _offGlyph;
        private readonly string _onGlyph;
        private readonly string _setKey;
        private readonly string _clearKey;
        private bool? _shown;

        public CardFlag Flag { get; }

        public FlagButton(CardFlag flag, string offGlyph, string onGlyph, string setKey, string clearKey)
        {
            Flag = flag;
            _offGlyph = offGlyph;
            _onGlyph = onGlyph;
            _setKey = setKey;
            _clearKey = clearKey;
            Width = 28;
            Height = 28;
            Padding = new Thickness(0);
            BorderThickness = new Thickness(0);
            CornerRadius = (CornerRadius)Application.Current.Resources["MediaOverlayBadgeCornerRadius"];
            Background = (Brush)Application.Current.Resources["MediaOverlayBadgeBackgroundBrush"];
            Content = _icon;
            Show(false);
        }

        /// <summary>Shows whether the flag is set; the tooltip says what a click will do.</summary>
        public void Show(bool on)
        {
            if (_shown == on) return;
            _shown = on;
            _icon.Glyph = on ? _onGlyph : _offGlyph;
            var text = UiText.Get(on ? _clearKey : _setKey);
            ToolTipService.SetToolTip(this, text);
            AutomationProperties.SetName(this, text);
        }
    }
}

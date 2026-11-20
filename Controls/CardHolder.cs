using System.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// Random discovery's hand of cards, standing in a three-tier rack like a
/// card holder on a table: the tiers overlap back to front, each fanned a
/// little, and a tray in front hides the bottom of the front row.
/// </summary>
/// <remarks>
/// The user's request, with a photo of a poker card holder. The hand used to
/// be one more grid of thumbnails, the same as the library's, so dealing a
/// hand did not look like anything. A card under the pointer (or with focus)
/// is drawn up out of the rack and comes to the front; a click or Enter opens
/// it. All sizes are in UiMetrics (Holder*), the moves in CardMotion.
/// </remarks>
internal sealed class CardHolder : UserControl
{
    private readonly Canvas _canvas = new();
    private readonly Border _tray = new()
    {
        CornerRadius = new CornerRadius(UiMetrics.HolderTrayCorner),
        Background = (Brush)Application.Current.Resources["MediaCardBackgroundBrush"],
        BorderBrush = (Brush)Application.Current.Resources["MediaCardStrokeBrush"],
        BorderThickness = new Thickness(1),
        IsHitTestVisible = false,
    };
    private readonly List<Slot> _slots = [];
    private IReadOnlyList<DiscoveryItem> _items = [];

    /// <summary>A card was clicked, or Enter pressed on it.</summary>
    public event Action<DiscoveryItem>? CardInvoked;

    public CardHolder()
    {
        AutomationProperties.SetAutomationId(this, "DiscoveryHolder");
        Content = _canvas;
        _canvas.Children.Add(_tray);
        Canvas.SetZIndex(_tray, TrayZ);
        SizeChanged += (_, _) => Arrange();
        Unloaded += (_, _) => { foreach (var slot in _slots) slot.Detach(); };
    }

    private const int TrayZ = 500;
    private const int LiftedZ = 1000;

    /// <summary>
    /// Shows a hand. A different set of cards is dealt in card by card; the
    /// same cards (a refresh that only replaced their objects) just update.
    /// </summary>
    public void Show(IReadOnlyList<DiscoveryItem> items)
    {
        var same = items.Count == _items.Count
            && items.Select(item => item.Card.FilePath).SequenceEqual(_items.Select(item => item.Card.FilePath), StringComparer.OrdinalIgnoreCase);
        _items = items;
        if (same)
        {
            for (var i = 0; i < items.Count; i++) _slots[i].Bind(items[i]);
            return;
        }

        foreach (var slot in _slots)
        {
            slot.Detach();
            _canvas.Children.Remove(slot.Root);
        }
        _slots.Clear();
        foreach (var item in items)
        {
            var slot = new Slot(this);
            slot.Bind(item);
            _slots.Add(slot);
            _canvas.Children.Add(slot.Root);
        }
        _tray.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Arrange();

        // Dealt back tier first, left to right, as cards go into a rack.
        var height = CardHeight();
        for (var i = 0; i < _slots.Count; i++)
            CardMotion.Deal(_slots[i].Root, (float)(height * 0.6), CardMotion.DealStagger * i);
    }

    /// <summary>Moves keyboard focus to the first card (after a new hand).</summary>
    public void FocusFirst()
    {
        if (_slots.Count > 0) _slots[0].Root.Focus(FocusState.Programmatic);
    }

    // ------------------------------------------------------------ layout

    private double _cardWidth;

    private double CardHeight() => _cardWidth * UiMetrics.HolderCardAspect;

    /// <summary>How many cards each tier holds, back first: the back tier takes the remainder.</summary>
    private static int[] TierSizes(int count)
    {
        var tiers = Math.Min(UiMetrics.HolderTiers, Math.Max(1, count));
        var sizes = new int[tiers];
        for (var t = 0; t < tiers; t++) sizes[t] = count / tiers;
        for (var t = 0; t < count % tiers; t++) sizes[t]++;
        return sizes;
    }

    private void Arrange()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0 || _slots.Count == 0) return;

        var tiers = TierSizes(_slots.Count);
        var widest = tiers.Max();
        var spanInCards = 1 + (widest - 1) * UiMetrics.HolderStep;
        // Height in card heights: room to lift, the card, the drops between tiers, the tray below.
        var tallInCards = UiMetrics.HolderLift + 1 + (tiers.Length - 1) * UiMetrics.HolderTierDrop + UiMetrics.HolderTrayBelow;
        var byWidth = (width - 2 * UiMetrics.HolderTrayPadding) / spanInCards;
        var byHeight = height / (tallInCards * UiMetrics.HolderCardAspect);
        _cardWidth = Math.Max(40, Math.Min(UiMetrics.HolderMaxCardWidth, Math.Min(byWidth, byHeight)));
        var cardHeight = CardHeight();

        var blockHeight = tallInCards * cardHeight;
        var top = Math.Max(0, (height - blockHeight) / 2) + UiMetrics.HolderLift * cardHeight;

        var index = 0;
        for (var tier = 0; tier < tiers.Length; tier++)
        {
            var count = tiers[tier];
            var rowWidth = _cardWidth * (1 + (count - 1) * UiMetrics.HolderStep);
            var left = (width - rowWidth) / 2;
            var rowTop = top + tier * UiMetrics.HolderTierDrop * cardHeight;
            for (var j = 0; j < count; j++, index++)
            {
                var offset = j - (count - 1) / 2.0;
                var slot = _slots[index];
                slot.Place(
                    left + j * UiMetrics.HolderStep * _cardWidth,
                    rowTop + offset * offset * UiMetrics.HolderArc * cardHeight,
                    _cardWidth, cardHeight,
                    offset * UiMetrics.HolderFanDegrees,
                    // Later tiers in front, and in a tier each card over the one on its left.
                    tier * 100 + j);
            }
        }

        var frontTop = top + (tiers.Length - 1) * UiMetrics.HolderTierDrop * cardHeight;
        var trayWidth = _cardWidth * spanInCards + 2 * UiMetrics.HolderTrayPadding;
        _tray.Width = Math.Min(width, trayWidth);
        _tray.Height = (UiMetrics.HolderTrayCover + UiMetrics.HolderTrayBelow) * cardHeight;
        Canvas.SetLeft(_tray, (width - _tray.Width) / 2);
        Canvas.SetTop(_tray, frontTop + (1 - UiMetrics.HolderTrayCover) * cardHeight);
    }

    // ------------------------------------------------------------ one card

    private sealed class Slot
    {
        private readonly CardHolder _owner;
        private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
        private readonly Border _joker = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
        };
        private readonly RotateTransform _turn = new();
        private DiscoveryItem? _item;
        private int _z;

        public Grid Root { get; }

        public Slot(CardHolder owner)
        {
            _owner = owner;
            _joker.Style = (Style)Application.Current.Resources["MediaOverlayBadgeStyle"];
            var overlay = (Brush)Application.Current.Resources["MediaOverlayForegroundBrush"];
            var jokerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            jokerRow.Children.Add(new FontIcon { Glyph = Glyphs.Joker, FontSize = 12, Foreground = overlay });
            jokerRow.Children.Add(new TextBlock { Text = UiText.Get("Discovery_Joker"), FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = overlay });
            _joker.Child = jokerRow;

            var face = new Border
            {
                CornerRadius = new CornerRadius(UiMetrics.HolderCardCorner),
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["MediaCardStrokeBrush"],
                BorderThickness = new Thickness(1),
                Child = _image,
            };
            Root = new Grid
            {
                IsTabStop = true,
                UseSystemFocusVisuals = true,
                RenderTransform = _turn,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            };
            Root.Children.Add(face);
            Root.Children.Add(_joker);

            Root.PointerEntered += (_, _) => Lift(true);
            // Focus lifts a card only when it came from the keyboard: the page
            // puts focus on the first card after dealing (programmatic), and a
            // click focuses too, and either left that card standing up over the
            // rack with nothing pointing at it (user report).
            Root.PointerExited += (_, _) => { if (Root.FocusState != FocusState.Keyboard) Lift(false); };
            Root.GotFocus += (_, _) => { if (Root.FocusState == FocusState.Keyboard) Lift(true); };
            Root.LostFocus += (_, _) => Lift(false);
            Root.Tapped += (_, e) => { e.Handled = true; Invoke(); };
            Root.KeyDown += (_, e) =>
            {
                if (e.Key is VirtualKey.Enter or VirtualKey.Space) { e.Handled = true; Invoke(); }
            };
        }

        public void Bind(DiscoveryItem item)
        {
            if (_item?.Card is { } old && !ReferenceEquals(old, item.Card)) old.PropertyChanged -= Card_Changed;
            if (!ReferenceEquals(_item?.Card, item.Card)) item.Card.PropertyChanged += Card_Changed;
            _item = item;
            _image.Source = LibraryBrowser.Thumbnail(item.Card.ThumbnailUri);
            _joker.Visibility = item.IsJoker ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(Root, item.Card.FileName);
            ToolTipService.SetToolTip(Root, item.Card.FileName);
        }

        public void Detach()
        {
            if (_item?.Card is { } card) card.PropertyChanged -= Card_Changed;
        }

        private void Card_Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CardBase.ThumbnailUri) && _item is { } item)
                _image.Source = LibraryBrowser.Thumbnail(item.Card.ThumbnailUri);
        }

        public void Place(double x, double y, double width, double height, double angle, int z)
        {
            Root.Width = width;
            Root.Height = height;
            Canvas.SetLeft(Root, x);
            Canvas.SetTop(Root, y);
            // Turned about the bottom centre, where a card rests in the rack.
            _turn.CenterX = width / 2;
            _turn.CenterY = height;
            _turn.Angle = angle;
            _z = z;
            Canvas.SetZIndex(Root, z);
        }

        private void Lift(bool up)
        {
            Canvas.SetZIndex(Root, up ? LiftedZ : _z);
            CardMotion.Lift(Root, up, (float)(_owner.CardHeight() * UiMetrics.HolderLift));
        }

        private void Invoke()
        {
            if (_item is { } item) _owner.CardInvoked?.Invoke(item);
        }
    }
}

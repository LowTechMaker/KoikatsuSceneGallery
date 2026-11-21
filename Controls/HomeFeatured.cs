using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ToggleButton = Microsoft.UI.Xaml.Controls.Primitives.ToggleButton;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The home page's featured scenes: one large at a time with what it is and
/// what to do with it, turning to the next on its own every few seconds, and a
/// strip of small pictures under it to pick one directly.
/// </summary>
/// <remarks>
/// It replaces the pair to choose between (user's pick: a featured card at
/// the top of a dashboard), and shows several since (user request): the first
/// <see cref="UiMetrics.HomeFeaturedCount"/> of the dealt hand
/// (<see cref="HomeViewModel.Deal"/>), every <see cref="AppTiming.FeaturedCycle"/>,
/// holding still while the pointer is on it, the loupe is open or the page is
/// away. A new hand comes with the page's "new themes". The picture is drawn from the file itself,
/// not the thumbnail, because it is the one large picture on the page; the
/// same picture, faint, lies behind the whole band.
/// </remarks>
internal sealed class HomeFeatured : UserControl
{
    private readonly HomeViewModel _home;
    private readonly CardStateService _states = App.Services.GetRequiredService<CardStateService>();
    private readonly Image _picture = new() { Stretch = Stretch.UniformToFill };
    private readonly Image _backdrop = new() { Stretch = Stretch.UniformToFill, Opacity = UiMetrics.HomeFeaturedBackdropOpacity, IsHitTestVisible = false };
    private readonly Border _pictureFrame = new();
    private readonly TextBlock _heading = new()
    {
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };
    private readonly TextBlock _title = new()
    {
        Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        TextWrapping = TextWrapping.Wrap,
        MaxLines = 2,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock _author = new() { Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
    private readonly RatingBadge _rating = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _tags = new()
    {
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        TextWrapping = TextWrapping.Wrap,
        MaxLines = 2,
    };
    private readonly Button _open = new() { Content = UiText.Get("Inspector_Open"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    private readonly ToggleButton _queue = new();
    private readonly StackPanel _details = new() { Spacing = 6 };
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly List<(Border Frame, CardBase Card)> _stripItems = [];
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _cycle;
    private bool _hovered, _suspended;
    private float _turnShift = 24;
    private readonly Border _countdown = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(1.5),
    };
    private readonly TextBlock _empty = new()
    {
        Text = UiText.Get("Home_FeaturedEmpty"),
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(16),
    };
    private readonly Grid _content = new();
    private int _index;
    private CardBase? _card;

    /// <summary>Open was clicked (or the picture): the card, among the dealt hand.</summary>
    public event Action<HomeShelf, CardBase>? CardInvoked;

    public HomeFeatured(HomeViewModel home)
    {
        _home = home;
        AutomationProperties.SetAutomationId(this, "HomeFeatured");
        _heading.Text = UiText.Get("Home_Featured");

        _pictureFrame.CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"];
        _pictureFrame.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        _pictureFrame.Child = _picture;
        var pictureButton = new Button { Style = (Style)Application.Current.Resources["PlainTileButtonStyle"], Content = _pictureFrame };
        AutomationProperties.SetAutomationId(pictureButton, "HomeFeaturedPicture");
        pictureButton.Click += (_, _) => Open();
        ButtonDragOut.Enable(pictureButton, () => _card?.FilePath, "Home.Drag");

        AutomationProperties.SetAutomationId(_open, "HomeFeaturedOpen");
        _open.Click += (_, _) => Open();
        _queue.Content = IconLabel(Glyphs.Queue, UiText.Get("Home_FeaturedQueue"));
        AutomationProperties.SetAutomationId(_queue, "HomeFeaturedQueue");
        _queue.Click += async (_, _) =>
        {
            if (_card is { } card && await _states.ToggleAsync(card.FilePath, CardFlag.Queued) is null)
                AppToast.Show(UiText.Get("CardState_SaveFailed"), InfoBarSeverity.Error, key: "card-state");
            ShowQueued();
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_open);
        actions.Children.Add(_queue);

        _details.Children.Add(_heading);
        _details.Children.Add(_title);
        _details.Children.Add(_author);
        _details.Children.Add(_rating);
        _details.Children.Add(_tags);
        AutomationProperties.SetAutomationId(_strip, "HomeFeaturedStrip");
        var info = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(_details);
        info.Children.Add(actions);
        info.Children.Add(_strip);

        _content.ColumnSpacing = 20;
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MaxWidth = UiMetrics.HomeFeaturedMaxPictureWidth });
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        // Under the picture, a thin bar filling up to the next turn (CardMotion.Countdown):
        // it says the card changes by itself, and when; empty while held still.
        var countdownTrack = new Grid
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
        };
        countdownTrack.Children.Add(_countdown);
        var pictureColumn = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        pictureColumn.Children.Add(pictureButton);
        pictureColumn.Children.Add(countdownTrack);
        _content.Children.Add(pictureColumn);
        Grid.SetColumn(info, 1);
        _content.Children.Add(info);
        // The picture at the cards' own shape, as wide as its column lets it be.
        // Its height from the width the column really gives it (never more than
        // the maximum): measured before the column's cap applied, it came out
        // far too tall (user report).
        pictureButton.VerticalAlignment = VerticalAlignment.Center;
        pictureButton.SizeChanged += (_, e) =>
        {
            var width = Math.Min(e.NewSize.Width, UiMetrics.HomeFeaturedMaxPictureWidth);
            var height = width * UiMetrics.HolderCardAspect;
            if (Math.Abs(_pictureFrame.Height - height) > 0.5) _pictureFrame.Height = height;
        };

        var band = new Grid();
        band.Children.Add(new Border { Child = _backdrop, CornerRadius = (CornerRadius)Application.Current.Resources["OverlayCornerRadius"] });
        band.Children.Add(new Border { Padding = new Thickness(16), Child = _content });
        band.Children.Add(_empty);
        Content = new Border
        {
            CornerRadius = (CornerRadius)Application.Current.Resources["OverlayCornerRadius"],
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = band,
        };

        _cycle = DispatcherQueue.CreateTimer();
        _cycle.Interval = AppTiming.FeaturedCycle;
        _cycle.Tick += (_, _) => Select(_index + 1);
        // Holds still while the pointer is on it: someone reading it should not
        // have it change under them (user request).
        PointerEntered += (_, _) => { _hovered = true; UpdateCycle(); };
        PointerExited += (_, _) => { _hovered = false; UpdateCycle(); };
        PointerCanceled += (_, _) => { _hovered = false; UpdateCycle(); };

        _home.Deal.Cards.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(NewHand);
        _states.StateChanged += path => DispatcherQueue.TryEnqueue(() => { if (_card?.FilePath == path) ShowQueued(); });
        NewHand();
    }

    /// <summary>Stops turning (the page went away, or the loupe is open over it).</summary>
    public void Suspend() { _suspended = true; UpdateCycle(); }

    /// <summary>Turns again, from a full interval.</summary>
    public void Resume() { _suspended = false; UpdateCycle(); }

    private IReadOnlyList<CardBase> Featured() => [.. _home.Deal.Cards.Take(UiMetrics.HomeFeaturedCount)];

    private void UpdateCycle()
    {
        _cycle.Stop();
        var running = !_hovered && !_suspended && Featured().Count > 1;
        if (running) _cycle.Start();
        CardMotion.Countdown(_countdown, running ? AppTiming.FeaturedCycle : null);
    }

    /// <summary>The picture, for the loupe's flight; null when nothing is featured.</summary>
    public FrameworkElement? PictureOf(CardBase card) => ReferenceEquals(card, _card) ? _picture : null;

    private void Open()
    {
        if (_card is { } card) CardInvoked?.Invoke(_home.Deal, card);
    }

    /// <summary>A new hand: the strip is built again and the first card shown.</summary>
    private void NewHand()
    {
        _strip.Children.Clear();
        _stripItems.Clear();
        var cards = Featured();
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var index = i;
            var frame = new Border
            {
                Width = UiMetrics.HomeFeaturedStripWidth,
                Height = UiMetrics.HomeFeaturedStripWidth * UiMetrics.HolderCardAspect,
                CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
                BorderThickness = new Thickness(2),
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                Child = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    Source = new BitmapImage { DecodePixelWidth = UiMetrics.HomeFeaturedStripDecodeWidth, UriSource = new Uri(card.FilePath) },
                },
            };
            var button = new Button { Style = (Style)Application.Current.Resources["PlainTileButtonStyle"], Content = frame };
            AutomationProperties.SetName(button, card.FileName);
            ToolTipService.SetToolTip(button, card.FileName);
            button.Click += (_, _) => Select(index);
            _strip.Children.Add(button);
            _stripItems.Add((frame, card));
        }
        _strip.Visibility = cards.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _index = 0;
        Show();
        CardMotion.EnterStaggered([.. _strip.Children], rise: 6);
    }

    /// <summary>Shows the featured card at <paramref name="index"/> (wrapping), and starts the interval again.</summary>
    private void Select(int index)
    {
        var count = Featured().Count;
        if (count == 0) return;
        var next = ((index % count) + count) % count;
        // Forward comes in from the right, a pick further left from the left.
        _turnShift = next >= _index || (index > _index && next == 0) ? 24 : -24;
        _index = next;
        Show();
        UpdateCycle();
    }

    private void Show()
    {
        var cards = Featured();
        if (_index >= cards.Count) _index = 0;
        _card = cards.Count > 0 ? cards[_index] : null;
        for (var i = 0; i < _stripItems.Count; i++)
            _stripItems[i].Frame.BorderBrush = (Brush)Application.Current.Resources[i == _index ? "AccentFillColorDefaultBrush" : "ControlStrokeColorDefaultBrush"];
        _content.Visibility = _card is null ? Visibility.Collapsed : Visibility.Visible;
        _empty.Visibility = _card is null ? Visibility.Visible : Visibility.Collapsed;
        if (_card is not { } card) return;

        var info = _home.Describe(card);
        _title.Text = info.Title;
        _author.Text = info.Author ?? UiText.Get("SceneGallery_UnknownAuthor");
        _author.Visibility = Visibility.Visible;
        _rating.Text = card is SceneCard scene
            ? scene.Rating switch { SceneRating.R18G => RatingLabels.R18G, SceneRating.R18 => RatingLabels.R18, _ => RatingLabels.General }
            : "";
        _tags.Text = string.Join("  ·  ", info.Tags.Take(UiMetrics.HomeFeaturedTags));
        _tags.Visibility = info.Tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var source = new BitmapImage { DecodePixelWidth = UiMetrics.HomeFeaturedDecodeWidth, UriSource = new Uri(card.FilePath) };
        _picture.Source = source;
        _backdrop.Source = source;
        AutomationProperties.SetName(_open, $"{UiText.Get("Inspector_Open")} {info.Title}");
        ShowQueued();
        // The picture and the words change; the buttons and the strip stay put.
        CardMotion.Turn(_pictureFrame, _turnShift);
        CardMotion.Turn(_details, _turnShift);
        UpdateCycle();
    }

    private void ShowQueued()
    {
        if (_card is not { } card) return;
        _queue.IsChecked = _states.Has(card.FilePath, CardFlag.Queued);
    }

    private static StackPanel IconLabel(string glyph, string text) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            new FontIcon { Glyph = glyph, FontSize = UiMetrics.IconSize },
            new TextBlock { Text = text },
        },
    };
}

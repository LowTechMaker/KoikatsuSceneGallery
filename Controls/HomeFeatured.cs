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
/// The home page's card of the day: one scene, large, with what it is and
/// three things to do with it (open, queue, another one).
/// </summary>
/// <remarks>
/// It replaces the pair to choose between (user's pick: one featured card at
/// the top of a dashboard). The cards come from the dealt hand
/// (<see cref="HomeViewModel.Deal"/>): "another one" steps through it and
/// deals a fresh hand at its end. The picture is drawn from the file itself,
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
    private readonly Button _next = new();
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
        _next.Content = IconLabel(Glyphs.Shuffle, UiText.Get("Home_FeaturedNext"));
        AutomationProperties.SetAutomationId(_next, "HomeFeaturedNext");
        _next.Click += (_, _) => Next();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_open);
        actions.Children.Add(_queue);
        actions.Children.Add(_next);

        var info = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(_heading);
        info.Children.Add(_title);
        info.Children.Add(_author);
        info.Children.Add(_rating);
        info.Children.Add(_tags);
        info.Children.Add(new Border { Height = 4 });
        info.Children.Add(actions);

        _content.ColumnSpacing = 20;
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MaxWidth = UiMetrics.HomeFeaturedMaxPictureWidth });
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        _content.Children.Add(pictureButton);
        Grid.SetColumn(info, 1);
        _content.Children.Add(info);
        // The picture at the cards' own shape, as wide as its column lets it be.
        pictureButton.SizeChanged += (_, e) =>
        {
            var height = e.NewSize.Width * UiMetrics.HolderCardAspect;
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

        _home.Deal.Cards.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(Show);
        _states.StateChanged += path => DispatcherQueue.TryEnqueue(() => { if (_card?.FilePath == path) ShowQueued(); });
        Show();
    }

    /// <summary>The picture, for the loupe's flight; null when nothing is featured.</summary>
    public FrameworkElement? PictureOf(CardBase card) => ReferenceEquals(card, _card) ? _picture : null;

    private void Open()
    {
        if (_card is { } card) CardInvoked?.Invoke(_home.Deal, card);
    }

    private void Next()
    {
        _index++;
        if (_index >= _home.Deal.Cards.Count)
        {
            _index = 0;
            _home.DealAgain();   // Show follows through the collection change
            return;
        }
        Show();
    }

    private void Show()
    {
        var cards = _home.Deal.Cards;
        if (_index >= cards.Count) _index = 0;
        _card = cards.Count > 0 ? cards[_index] : null;
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
        CardMotion.Crossfade(_content);
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

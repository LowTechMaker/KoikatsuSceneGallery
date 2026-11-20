using KoikatsuSceneGallery.Converters;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Pages;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The pane beside a grid of cards that says what is selected and offers what
/// can be done with it, so a click selects instead of leaving the page.
/// </summary>
/// <remarks>
/// <para>
/// Three states: nothing selected (a hint), one card (its picture, title and
/// author, flags, rating, file actions and facts), several cards (a count and
/// the actions that apply to all of them).
/// </para>
/// <para>
/// It replaces the info column the three detail pages each built for
/// themselves, which had drifted apart (rating only on scenes, the metadata
/// panel only on characters). What it shows comes from the card model and the
/// shared helpers (<see cref="CardStateActions"/>, <see cref="CardRatingActions"/>,
/// <see cref="CardFileActions"/>, <see cref="CardMenu"/>), the same ones the
/// right-click menu uses, so a card offers the same things everywhere.
/// </para>
/// </remarks>
internal sealed class CardInspector : UserControl
{
    private const double PreviewHeight = 180;

    private readonly CardStateService _states = App.Services.GetRequiredService<CardStateService>();
    private readonly HomeViewModel? _home = App.Services.GetService<HomeViewModel>();
    private readonly IAppLogger _logger = App.Services.GetRequiredService<IAppLogger>();

    private readonly StackPanel _body = new() { Spacing = 16, Padding = new Thickness(16) };
    private readonly TextBlock _empty = Secondary(UiText.Get("Inspector_Empty"));

    // One card.
    private readonly StackPanel _single = new() { Spacing = 16 };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, MaxHeight = PreviewHeight };
    private readonly ProgressivePreview _previewLoader;
    private readonly TextBlock _title = new()
    {
        Style = (Style)Application.Current.Resources["SectionTitleTextBlockStyle"],
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
    };
    private readonly HyperlinkButton _author = new() { Padding = new Thickness(0) };
    private readonly ToggleButton _queue = FlagToggle(CardFlag.Queued);
    private readonly ToggleButton _like = FlagToggle(CardFlag.Liked);
    private readonly ToggleButton _hide = FlagToggle(CardFlag.Hidden);
    private readonly StackPanel _ratingSection = new() { Spacing = 8 };
    private readonly UserRatingPicker _rating = new(large: false);
    private readonly HyperlinkButton _ratingClear = new() { Padding = new Thickness(0), Content = UiText.Get("Detail_ClearUserRating.Content") };
    private readonly TextBlock _ratingStatus = Secondary("");
    private readonly CardActionBar _actions = new();
    private readonly StackPanel _kind = new() { Spacing = 8 };
    private readonly StackPanel _facts = new() { Spacing = 8 };
    private readonly MetadataPanel _metadata = new();
    private readonly Button _open = new() { Content = UiText.Get("Inspector_Open"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };

    // Several cards.
    private readonly StackPanel _many = new() { Spacing = 12 };
    private readonly TextBlock _manyTitle = new() { Style = (Style)Application.Current.Resources["SectionTitleTextBlockStyle"] };
    private readonly StackPanel _manyActions = new() { Spacing = 8 };

    private IReadOnlyList<CardBase> _cards = [];
    private string? _ratingHash;
    private int _ratingVersion;
    private bool _subscribed;

    /// <summary>The frame "go to author" navigates; set by the host page.</summary>
    public Func<Frame?>? Frame { get; set; }

    /// <summary>The user asked to open the one selected card (the host decides how).</summary>
    public event Action<CardBase>? OpenRequested;

    /// <summary>What is shown now.</summary>
    public IReadOnlyList<CardBase> Cards => _cards;

    public CardInspector()
    {
        _previewLoader = new ProgressivePreview(_preview);
        AutomationProperties.SetAutomationId(this, "CardInspector");
        AutomationProperties.SetName(this, UiText.Get("Inspector_Name"));

        var surface = new Border
        {
            CornerRadius = (CornerRadius)Application.Current.Resources["OverlayCornerRadius"],
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
        Content = surface;

        BuildSingle();
        BuildMany();
        _body.Children.Add(_empty);
        _body.Children.Add(_single);
        _body.Children.Add(_many);

        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) =>
        {
            if (VisualTreeHelper.GetParent(this) is not null) return;
            Unsubscribe();
        };
        Show([]);
    }

    /// <summary>Shows the selection: none, one card or several.</summary>
    public void Show(IReadOnlyList<CardBase> cards)
    {
        _cards = cards;
        _empty.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _single.Visibility = cards.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
        _many.Visibility = cards.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ++_ratingVersion; // a rating still loading belongs to the old selection
        if (cards.Count == 1) ShowOne(cards[0]);
        else if (cards.Count > 1) ShowMany(cards);
        else _metadata.FilePath = null;
    }

    // ------------------------------------------------------------ one card

    private void BuildSingle()
    {
        var frame = new Border
        {
            CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            Height = PreviewHeight,
            Child = _preview,
        };
        AutomationProperties.SetAutomationId(_preview, "CardInspector_Preview");
        // A double click on the picture opens the card, like one on the tile.
        frame.DoubleTapped += (_, _) => RequestOpen();
        _single.Children.Add(frame);

        var heading = new StackPanel { Spacing = 2 };
        AutomationProperties.SetAutomationId(_title, "CardInspector_Title");
        heading.Children.Add(_title);
        AutomationProperties.SetAutomationId(_author, "CardInspector_Author");
        _author.Click += (_, _) => GoToAuthor();
        heading.Children.Add(_author);
        _single.Children.Add(heading);

        var flags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var toggle in new[] { _queue, _like, _hide })
        {
            var flag = (CardFlag)toggle.Tag;
            toggle.Click += (_, _) => Toggle(flag);
            flags.Children.Add(toggle);
        }
        AutomationProperties.SetAutomationId(_open, "CardInspector_Open");
        _open.Click += (_, _) => RequestOpen();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(flags);
        Grid.SetColumn(_open, 1);
        row.Children.Add(_open);
        _single.Children.Add(row);

        _ratingSection.Children.Add(Secondary(UiText.Get("Detail_UserRating.Text")));
        _ratingSection.Children.Add(_rating);
        var ratingFoot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        AutomationProperties.SetAutomationId(_ratingClear, "CardInspector_ClearRating");
        _ratingClear.Click += (_, _) => UiEventGuard.Run(_logger, "Inspector.ClearRating", () => SaveRatingAsync(null));
        ratingFoot.Children.Add(_ratingClear);
        _ratingStatus.VerticalAlignment = VerticalAlignment.Center;
        ratingFoot.Children.Add(_ratingStatus);
        _ratingSection.Children.Add(ratingFoot);
        _rating.RatingChosen += value => UiEventGuard.Run(_logger, "Inspector.SetRating", () => SaveRatingAsync(value));
        _single.Children.Add(_ratingSection);

        AutomationProperties.SetAutomationId(_actions, "CardInspector_Actions");
        _single.Children.Add(_actions);
        _single.Children.Add(_kind);
        _single.Children.Add(new TextBlock
        {
            Text = UiText.Get("Detail_FileInfo.Text"),
            Style = (Style)Application.Current.Resources["SectionTitleTextBlockStyle"],
        });
        _single.Children.Add(_facts);
        _single.Children.Add(_metadata);
    }

    private void ShowOne(CardBase card)
    {
        _previewLoader.Show(card, 640);

        var info = _home?.Describe(card);
        _title.Text = card switch
        {
            CharacterCard { CharacterName.Length: > 0 } chara => chara.CharacterName,
            CoordinateCard { CoordinateName.Length: > 0 } coord => coord.CoordinateName,
            _ => info?.Title ?? card.FileName,
        };
        var author = (card as IAuthorOwner)?.Author;
        _author.Content = author?.Name ?? UiText.Get("SceneGallery_UnknownAuthor");
        _author.IsEnabled = author is not null && CardMenu.FindAuthor(author) is not null;

        UpdateFlags();

        var links = CardOrigin.LinksFor(card);
        _actions.FilePath = card.FilePath;
        _actions.PixivUrl = links.PixivUrl;
        _actions.BepisDbUrl = links.BepisDbUrl;

        _kind.Children.Clear();
        switch (card)
        {
            case CharacterCard chara:
                _kind.Children.Add(Fact("Detail_CharaName.Text", chara.CharacterName));
                _kind.Children.Add(Fact("Detail_CharaGame.Text", chara.Game.ToString()));
                break;
            case CoordinateCard coord:
                _kind.Children.Add(Fact("Detail_CoordName.Text", coord.CoordinateName));
                break;
        }
        _kind.Visibility = _kind.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _facts.Children.Clear();
        _facts.Children.Add(Fact("Detail_FileName.Text", card.FileName));
        _facts.Children.Add(Fact("Detail_FileSize.Text", FileSizeConverter.Format(card.FileSize)));
        if (card.Width > 0 && card.Height > 0)
            _facts.Children.Add(Fact("Detail_Resolution.Text", $"{card.Width} × {card.Height}"));
        _facts.Children.Add(Fact("Detail_DateModified.Text", card.DateModified.ToString("g")));
        _facts.Children.Add(Fact("Detail_FilePath.Text", card.FilePath));
        if (links.PixivArtworkId is { } pixiv) _facts.Children.Add(Fact("Detail_PixivArtworkId.Text", pixiv));
        if (links.BepisDbId is { } bepis) _facts.Children.Add(Fact("Detail_BepisDbId.Text", bepis));

        // The card's embedded data: characters and outfits carry it, scenes do not.
        var hasMetadata = card is CharacterCard or CoordinateCard;
        _metadata.Visibility = hasMetadata ? Visibility.Visible : Visibility.Collapsed;
        _metadata.FilePath = hasMetadata ? card.FilePath : null;

        if (card is SceneCard scene)
        {
            _ratingSection.Visibility = Visibility.Visible;
            UiEventGuard.Run(_logger, "Inspector.LoadRating", () => LoadRatingAsync(scene));
        }
        else
        {
            _ratingSection.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadRatingAsync(SceneCard card)
    {
        var version = _ratingVersion;
        _ratingHash = null;
        _rating.SetEnabled(false);
        ShowRating(null);
        var (hash, rating) = await CardRatingActions.LoadAsync(card);
        if (version != _ratingVersion) return;
        if (hash is null)
        {
            _ratingStatus.Text = UiText.Get("Detail_UserRatingUnavailable");
            return;
        }
        _ratingHash = hash;
        _rating.SetEnabled(true);
        ShowRating(rating);
    }

    private async Task SaveRatingAsync(int? rating)
    {
        if (_cards is not [SceneCard card] || _ratingHash is not { } hash) return;
        var version = _ratingVersion;
        var stored = await CardRatingActions.SaveAsync(card, hash, rating);
        if (version != _ratingVersion) return;
        ShowRating(stored);
    }

    private void ShowRating(int? rating)
    {
        _rating.Rating = rating;
        _ratingClear.Visibility = rating is null ? Visibility.Collapsed : Visibility.Visible;
        _ratingStatus.Text = rating is null && _ratingHash is not null ? UiText.Get("Detail_UserRatingNone") : "";
    }

    /// <summary>Sets a rating from a digit key (CardKeys), as a click on the picker would.</summary>
    public void RateFromKey(int rating)
    {
        if (_cards.Count == 1) UiEventGuard.Run(_logger, "Inspector.SetRating", () => SaveRatingAsync(rating));
        else if (_cards.Count > 1) RateMany(rating);
    }

    private void GoToAuthor()
    {
        if (_cards is not [var card] || (card as IAuthorOwner)?.Author is not { } author) return;
        if (CardMenu.FindAuthor(author) is { } summary)
            Frame?.Invoke()?.Navigate(typeof(AuthorDetailPage), new AuthorDetailNavigationParameter(summary));
    }

    private void RequestOpen()
    {
        if (_cards is [var card]) OpenRequested?.Invoke(card);
    }

    // ------------------------------------------------------------ several cards

    private void BuildMany()
    {
        AutomationProperties.SetAutomationId(_manyTitle, "CardInspector_Count");
        _many.Children.Add(_manyTitle);
        _many.Children.Add(_manyActions);
    }

    private void ShowMany(IReadOnlyList<CardBase> cards)
    {
        _metadata.FilePath = null;
        _manyTitle.Text = UiText.Format("Inspector_SelectedMany", cards.Count);
        _manyActions.Children.Clear();
        var paths = cards.Select(card => card.FilePath).ToArray();
        foreach (var flag in new[] { CardFlag.Queued, CardFlag.Liked, CardFlag.Hidden })
        {
            var button = WideButton(
                $"{CardStateActions.Label(paths[0], flag)} ({CardKeys.Letter(flag)})",
                $"CardInspector_Many{flag}");
            button.Click += (_, _) => Toggle(flag);
            _manyActions.Children.Add(button);
        }

        var scenes = cards.OfType<SceneCard>().ToArray();
        if (scenes.Length > 0)
        {
            var rate = new DropDownButton
            {
                Content = UiText.Format("CardMenu_RateMany", scenes.Length),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            AutomationProperties.SetAutomationId(rate, "CardInspector_ManyRate");
            var menu = new MenuFlyout();
            for (var i = 0; i < CardRatingActions.Labels.Length; i++)
            {
                var value = i;
                var item = new MenuFlyoutItem { Text = $"{CardRatingActions.Labels[i]} ({i + 1})" };
                item.Click += (_, _) => RateMany(value);
                menu.Items.Add(item);
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            var clear = new MenuFlyoutItem { Text = UiText.Get("CardMenu_RateClear") };
            clear.Click += (_, _) => RateMany(null);
            menu.Items.Add(clear);
            rate.Flyout = menu;
            _manyActions.Children.Add(rate);
        }

        var copy = WideButton(UiText.Format("CardMenu_CopyFiles", cards.Count), "CardInspector_ManyCopy");
        copy.Click += (_, _) => UiEventGuard.Run(_logger, "Inspector.CopyFiles",
            () => CardFileActions.CopyFilesAsync(paths, "Inspector.CopyFiles"));
        _manyActions.Children.Add(copy);
    }

    private void RateMany(int? rating)
    {
        var scenes = _cards.OfType<SceneCard>().ToArray();
        if (scenes.Length == 0) return;
        UiEventGuard.Run(_logger, "Inspector.RateMany", () => CardRatingActions.RateManyAsync(scenes, rating));
    }

    // ------------------------------------------------------------ flags

    /// <summary>Toggles a flag on everything shown, as the buttons and the Q / F / X keys do.</summary>
    public void Toggle(CardFlag flag)
    {
        if (_cards.Count == 0) return;
        string[] paths = [.. _cards.Select(card => card.FilePath)];
        UiEventGuard.Run(_logger, "Inspector.Toggle", () => CardStateActions.ToggleAsync(paths, flag));
    }

    private void UpdateFlags()
    {
        if (_cards is not [var card]) return;
        foreach (var toggle in new[] { _queue, _like, _hide })
        {
            var flag = (CardFlag)toggle.Tag;
            var on = _states.Has(card.FilePath, flag);
            toggle.IsChecked = on;
            var label = $"{CardStateActions.Label(card.FilePath, flag)} ({CardKeys.Letter(flag)})";
            ToolTipService.SetToolTip(toggle, label);
            AutomationProperties.SetName(toggle, label);
        }
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        _states.StateChanged += State_Changed;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        _states.StateChanged -= State_Changed;
    }

    // Raised on whatever thread wrote the file.
    private void State_Changed(string path) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_cards.Any(card => string.Equals(card.FilePath, path, StringComparison.OrdinalIgnoreCase))) return;
        if (_cards.Count == 1) UpdateFlags();
        else ShowMany(_cards); // the labels say set or clear, after the first card
    });

    // ------------------------------------------------------------ pieces

    private static ToggleButton FlagToggle(CardFlag flag)
    {
        var glyph = flag switch
        {
            CardFlag.Queued => "", // Pin
            CardFlag.Liked => "",  // Heart
            _ => "",               // Clear
        };
        var toggle = new ToggleButton
        {
            Tag = flag,
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Padding = new Thickness(8, 6, 8, 6),
        };
        AutomationProperties.SetAutomationId(toggle, $"CardInspector_{flag}");
        AutomationProperties.SetAcceleratorKey(toggle, CardKeys.Letter(flag));
        return toggle;
    }

    private static Button WideButton(string text, string automationId)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }

    private static StackPanel Fact(string labelKey, string value)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(Secondary(UiText.Get(labelKey)));
        panel.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        return panel;
    }

    private static TextBlock Secondary(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        TextWrapping = TextWrapping.Wrap,
    };
}

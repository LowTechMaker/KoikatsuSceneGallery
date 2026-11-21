using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// What the details pane says about a character card beyond its file: the
/// character read from the card (name, sex, game, Madevil), the character's
/// other versions, and the version annotation (kind, superseded, note, which
/// character it is filed under).
/// </summary>
/// <remarks>
/// Moved here from the character detail page (wave 15) when the three detail
/// pages became one viewer; the behaviour is that page's, including saving a
/// note on Enter or when the box loses focus rather than per keystroke, and
/// rebuilding the version list from disk after a delete.
/// </remarks>
internal sealed class CharacterVersionsPanel : StackPanel
{
    private readonly CharacterGalleryViewModel _gallery = App.Services.GetRequiredService<CharacterGalleryViewModel>();
    private readonly IAppLogger _logger = App.Services.GetRequiredService<IAppLogger>();

    private readonly StackPanel _info = new() { Spacing = 8 };
    private readonly ProgressRing _parsing = new() { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, IsActive = true };
    private readonly StackPanel _versionsSection = new() { Spacing = 8 };
    private readonly TextBlock _versionsPosition = Caption("");
    private readonly StackPanel _versions = new() { Spacing = 4 };
    private readonly ComboBox _kind = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _superseded = new() { Content = UiText.Get("Detail_VersionSuperseded.Content") };
    private readonly TextBox _note = new()
    {
        Header = UiText.Get("Detail_VersionNote.Header"),
        PlaceholderText = UiText.Get("Detail_VersionNote.PlaceholderText"),
    };
    private readonly TextBlock _groupOverride = Caption("");

    private CharacterCard? _card;
    private string _fullName = "";
    private CancellationTokenSource? _parse;
    private bool _suppressWrites;
    private bool _subscribed;

    /// <summary>The user chose another version from the list: the host shows that card.</summary>
    public event Action<CharacterCard>? VersionChosen;

    /// <summary>The card shown was deleted and none of the character's versions is left.</summary>
    public event Action? CharacterGone;

    public CharacterVersionsPanel()
    {
        Spacing = 16;

        var info = new StackPanel { Spacing = 8 };
        info.Children.Add(Section("Detail_CharaInfo.Text"));
        info.Children.Add(_parsing);
        info.Children.Add(_info);
        Children.Add(info);

        _versionsSection.Children.Add(Section("Detail_VersionHistory.Text"));
        _versionsSection.Children.Add(_versionsPosition);
        _versionsSection.Children.Add(_versions);
        _versionsSection.Visibility = Visibility.Collapsed;
        Children.Add(_versionsSection);

        // The annotation is shown for a single version too: the first card of a
        // character has to be markable, or a what-if version could never be made.
        var annotation = new StackPanel { Spacing = 8 };
        annotation.Children.Add(Section("Detail_VersionAnnotation.Text"));
        _kind.Items.Add(new ComboBoxItem { Content = UiText.Get("Detail_VersionKind_Current.Content") });
        _kind.Items.Add(new ComboBoxItem { Content = UiText.Get("Detail_VersionKind_Alternate.Content") });
        AutomationProperties.SetAutomationId(_kind, "CharaDetailVersionKindBox");
        AutomationProperties.SetAutomationId(_superseded, "CharaDetailVersionSupersededBox");
        AutomationProperties.SetAutomationId(_note, "CharaDetailVersionNoteBox");
        annotation.Children.Add(_kind);
        annotation.Children.Add(_superseded);
        annotation.Children.Add(_note);
        var assign = new Button { Content = UiText.Get("Detail_AssignGroup.Content"), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(assign, "CharaDetailAssignGroupButton");
        annotation.Children.Add(assign);
        annotation.Children.Add(_groupOverride);
        Children.Add(annotation);

        _kind.SelectionChanged += (_, _) =>
        {
            if (_suppressWrites || _card is not { } card || _kind.SelectedIndex < 0) return;
            Save(card, (CharacterVersionKind)_kind.SelectedIndex, _superseded.IsChecked == true, _note.Text);
        };
        _superseded.Checked += (_, _) => SupersededChanged();
        _superseded.Unchecked += (_, _) => SupersededChanged();
        _note.LostFocus += (_, _) => SaveNoteIfChanged();
        _note.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            SaveNoteIfChanged();
            e.Handled = true;
        };
        assign.Click += (_, _) => UiEventGuard.Run(_logger, "Inspector.AssignGroup", AssignGroupAsync);

        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) =>
        {
            if (VisualTreeHelper.GetParent(this) is not null) return;
            Unsubscribe();
            PageCancellation.Stop(ref _parse);
        };
    }

    /// <summary>Shows a character card: parses it off the UI thread, then lists its versions.</summary>
    public void Show(CharacterCard card)
    {
        _card = card;
        LoadAnnotation(card);
        var token = PageCancellation.Restart(ref _parse).Token;
        UiEventGuard.Run(_logger, "Inspector.ParseCharacter", () => ParseAsync(card, token));
    }

    private async Task ParseAsync(CharacterCard card, CancellationToken token)
    {
        _parsing.Visibility = Visibility.Visible;
        _info.Visibility = Visibility.Collapsed;
        CharacterMetadata? meta;
        try
        {
            meta = await Task.Run(() => CharacterCardParser.TryParse(card.FilePath), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (token.IsCancellationRequested || !ReferenceEquals(_card, card)) return;

        meta ??= new CharacterMetadata(null, null, null, -1, GameVersion.Unknown, false);
        _fullName = string.IsNullOrWhiteSpace(meta.FullName) ? UiText.Get("Common_Unknown") : meta.FullName;
        _info.Children.Clear();
        _info.Children.Add(Fact("Detail_CharaName.Text", _fullName));
        _info.Children.Add(Fact("Detail_CharaSex.Text", UiText.Get(meta.Sex switch
        {
            CharacterMetadata.SexMale => "Common_Male",
            CharacterMetadata.SexFemale => "Common_Female",
            _ => "Common_Unknown",
        })));
        _info.Children.Add(Fact("Detail_CharaGame.Text", meta.Game switch
        {
            GameVersion.Koikatsu => "Koikatsu",
            GameVersion.KoikatsuSunshine => "Koikatsu Sunshine",
            _ => UiText.Get("Common_Unknown"),
        }));
        _info.Children.Add(Fact("Detail_CharaMadevil.Text", UiText.Get(meta.IsMadevil ? "Common_Yes" : "Common_No")));
        _parsing.Visibility = Visibility.Collapsed;
        _info.Visibility = Visibility.Visible;

        LoadVersions(card, _gallery.GetVersionKey(card) ?? meta.FullName ?? _fullName);
    }

    private void LoadVersions(CharacterCard card, string key)
    {
        var versions = _gallery.GetVersions(key);
        ShowVersions(card, versions is { Count: > 1 } ? [.. versions] : []);
    }

    private void ShowVersions(CharacterCard current, IReadOnlyList<CharacterCard> versions)
    {
        _versions.Children.Clear();
        _versionsSection.Visibility = versions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (versions.Count <= 1) return;
        var index = -1;
        for (var i = 0; i < versions.Count; i++)
        {
            var version = versions[i];
            if (ReferenceEquals(version, current)) index = i;
            _gallery.RequestThumbnail(version);
            _versions.Children.Add(VersionRow(version, ReferenceEquals(version, current), versions.Count));
        }
        _versionsPosition.Text = $"{index + 1} / {versions.Count}";
    }

    private Button VersionRow(CharacterCard version, bool current, int count)
    {
        var picture = new Image { Stretch = Stretch.Uniform };
        if (version.ThumbnailUri is { } uri) picture.Source = new BitmapImage { DecodePixelWidth = 96, UriSource = uri };
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (version.HasVersionLabel)
        {
            var label = Caption(version.VersionLabelText);
            label.Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
            label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            text.Children.Add(label);
        }
        if (version.HasVersionNote) text.Children.Add(new TextBlock
        {
            Text = version.VersionNote,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = version.FileTimestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });
        text.Children.Add(Caption(version.Resolution));

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Border
        {
            Height = 64,
            CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            Child = picture,
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var button = new Button
        {
            Content = row,
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Application.Current.Resources[current ? "AccentButtonStyle" : "SubtleButtonStyle"],
        };
        AutomationProperties.SetName(button, version.FileName);
        ToolTipService.SetToolTip(button, version.FilePath);
        button.Click += (_, _) =>
        {
            if (!ReferenceEquals(version, _card)) VersionChosen?.Invoke(version);
        };

        var menu = new MenuFlyout();
        var reveal = new MenuFlyoutItem { Text = UiText.Get("Detail_RevealVersion.Text"), Icon = new FontIcon { Glyph = Glyphs.OpenFolder } };
        reveal.Click += (_, _) => UiEventGuard.Run(_logger, "Inspector.RevealVersion", () => CardFileActions.OpenFolderAsync(version.FilePath));
        var delete = new MenuFlyoutItem { Text = UiText.Get("Detail_DeleteVersion.Text"), Icon = new FontIcon { Glyph = Glyphs.Delete } };
        delete.Click += (_, _) => UiEventGuard.Run(_logger, "Inspector.DeleteVersion", () => DeleteAsync(version, count));
        menu.Items.Add(reveal);
        menu.Items.Add(delete);
        button.ContextFlyout = menu;
        return button;
    }

    /// <summary>
    /// Removes a version through the recycle bin. The gallery hears of it from
    /// its file watcher only after a debounce, so the list is rebuilt here from
    /// what is on disk; otherwise the deleted card would stay listed.
    /// </summary>
    private async Task DeleteAsync(CharacterCard version, int count)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = UiText.Get("Detail_DeleteVersion_Title"),
            Content = new TextBlock
            {
                Text = UiText.Format("Detail_DeleteVersion_Body", version.FileName, Math.Max(0, count - 1)),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = UiText.Get("Detail_DeleteVersion_Confirm"),
            CloseButtonText = UiText.Get("LocalSources_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        var path = version.FilePath;
        if (!await Task.Run(() => RecycleBin.TryDelete(path)))
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = UiText.Get("Detail_DeleteVersion_FailedTitle"),
                Content = new TextBlock { Text = UiText.Format("Detail_DeleteVersion_Failed", version.FileName), TextWrapping = TextWrapping.Wrap },
                CloseButtonText = UiText.Get("LocalSources_Edit_Close"),
            }.ShowAsync();
            return;
        }

        var survivors = (_gallery.GetVersions(_gallery.GetVersionKey(version) ?? _fullName) ?? [])
            .Where(card => !string.Equals(card.FilePath, path, StringComparison.OrdinalIgnoreCase))
            .Where(card => File.Exists(card.FilePath))
            .ToArray();
        if (survivors.Length == 0)
        {
            CharacterGone?.Invoke();
            return;
        }
        if (_card is null || string.Equals(_card.FilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            VersionChosen?.Invoke(survivors[0]);
            return;
        }
        ShowVersions(_card, survivors);
    }

    // ------------------------------------------------------------ annotation

    private void LoadAnnotation(CharacterCard card)
    {
        _suppressWrites = true;
        try
        {
            _kind.SelectedIndex = (int)card.VersionKind;
            _superseded.IsChecked = card.IsSuperseded;
            _note.Text = card.VersionNote ?? "";
            ShowGroupOverride(card);
        }
        finally { _suppressWrites = false; }
    }

    private void ShowGroupOverride(CharacterCard card)
    {
        var group = card.CharacterGroupKey;
        _groupOverride.Text = string.IsNullOrWhiteSpace(group) ? "" : UiText.Format("Detail_GroupOverride", group);
        _groupOverride.Visibility = string.IsNullOrWhiteSpace(group) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SupersededChanged()
    {
        if (_suppressWrites || _card is not { } card) return;
        Save(card, card.VersionKind, _superseded.IsChecked == true, _note.Text);
    }

    /// <summary>Saved on commit, not per keystroke: each save is a file write and a re-rank.</summary>
    private void SaveNoteIfChanged()
    {
        if (_suppressWrites || _card is not { } card) return;
        var note = _note.Text.Trim();
        if (string.Equals(note, card.VersionNote ?? "", StringComparison.Ordinal)) return;
        Save(card, card.VersionKind, card.IsSuperseded, note);
    }

    private void Save(CharacterCard card, CharacterVersionKind kind, bool superseded, string? note)
        => UiEventGuard.Run(_logger, "Inspector.Annotate",
            () => _gallery.ApplyAnnotationAsync(card, kind, superseded, note, card.CharacterGroupKey));

    private async Task AssignGroupAsync()
    {
        if (_card is not { } card) return;
        var chosen = await CharacterGroupPicker.PickAsync(XamlRoot, _gallery.GetVersionGroupKeys(card.Author), _gallery.GetVersionKey(card));
        if (chosen is null) return;
        // An empty string means "use the card's own name".
        await _gallery.ApplyAnnotationAsync(card, card.VersionKind, card.IsSuperseded, card.VersionNote, chosen.Length == 0 ? null : chosen);
        ShowGroupOverride(card);
        LoadVersions(card, _gallery.GetVersionKey(card) ?? _fullName);
    }

    /// <summary>Whether the note box has focus: then letters are typing, not card keys.</summary>
    public bool IsEditing => _note.FocusState != FocusState.Unfocused;

    // ------------------------------------------------------------ the gallery's version index

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        _gallery.VersionIndexChanged += OnVersionIndexChanged;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        _gallery.VersionIndexChanged -= OnVersionIndexChanged;
    }

    // Another version of this character was added, removed or re-annotated.
    private void OnVersionIndexChanged(string key) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_card is not { } card || _info.Visibility != Visibility.Visible) return;
        var own = _gallery.GetVersionKey(card) ?? _fullName;
        if (!string.Equals(own, key, StringComparison.OrdinalIgnoreCase)) return;
        LoadVersions(card, key);
    });

    // ------------------------------------------------------------ pieces

    private static TextBlock Section(string key) => new()
    {
        Text = UiText.Get(key),
        Style = (Style)Application.Current.Resources["SectionTitleTextBlockStyle"],
    };

    private static StackPanel Fact(string labelKey, string value)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(Caption(UiText.Get(labelKey)));
        panel.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        return panel;
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        TextWrapping = TextWrapping.Wrap,
    };
}

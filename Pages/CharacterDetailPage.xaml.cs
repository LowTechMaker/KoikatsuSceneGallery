using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class CharacterDetailPage : Page
{
    private readonly ViewerChrome _viewer;
    public CharacterDetailViewModel ViewModel { get; } = new();

    private static readonly ResourceLoader ResLoader = new();
    private CancellationTokenSource? _metadataCts;
    private AuthorKey? _authorScope;

    public CharacterDetailPage()
    {
        InitializeComponent();
        _viewer = new(this, PreviewImage, () => ViewModel.Card, card => ShowCard((CharacterCard)card));

        // A version opened from the list below is not in the gallery list the
        // user arrived from, so the header falls back to this character's own
        // version numbering rather than reporting nothing found.
        _viewer.FallbackPosition = () => ViewModel.HasMultipleVersions
            ? (ViewModel.VersionIndex, ViewModel.TotalVersions)
            : null;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _viewer.SetContext(BrowseContexts.From(e.Parameter));
        var galleryViewModel = App.Services.GetRequiredService<CharacterGalleryViewModel>();
        galleryViewModel.ActivateThumbnailRequests();
        galleryViewModel.VersionIndexChanged += OnVersionIndexChanged;
        galleryViewModel.CardsReloaded += OnCardsReloaded;
        switch (e.Parameter)
        {
            case BrowseNavigation navigation:
                ShowCard((CharacterCard)navigation.Card);
                break;
            case AuthorScopedCharacterNavigationParameter scoped:
                _authorScope = scoped.AuthorKey;
                ShowCard(scoped.Card);
                break;
            case CharacterCard card:
                _authorScope = null;
                ShowCard(card);
                break;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        var galleryViewModel = App.Services.GetRequiredService<CharacterGalleryViewModel>();
        galleryViewModel.CancelPendingWork();
        PageCancellation.Stop(ref _metadataCts);
        galleryViewModel.VersionIndexChanged -= OnVersionIndexChanged;
        galleryViewModel.CardsReloaded -= OnCardsReloaded;
    }

    private void OnVersionIndexChanged(string characterName)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(Frame?.Content, this)) return;
            if (_viewer.RecoverIfMissing()) return;
            if (ViewModel.Card == null || !ViewModel.MetadataLoaded) return;
            var gallery = App.Services.GetRequiredService<CharacterGalleryViewModel>();
            // Compare against the key the card is filed under, not its name:
            // the two differ once the user attaches a card to another
            // character, and the index compares keys case-insensitively.
            var key = gallery.GetVersionKey(ViewModel.Card) ?? ViewModel.FullName;
            if (!string.Equals(key, characterName, StringComparison.OrdinalIgnoreCase)) return;

            var versions = gallery.GetVersions(characterName);
            if (versions != null && versions.Contains(ViewModel.Card))
            {
                LoadVersions(ViewModel.Card, characterName);
            }
            else if (versions != null && versions.Count > 0)
            {
                ShowCard(versions[0]);
            }
            else
            {
                if (Frame.CanGoBack) Frame.GoBack();
            }
        });
    }

    private void OnCardsReloaded()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(Frame?.Content, this) || ViewModel.Card is not { } current)
                return;

            DetailNavigationHelper.RefreshAfterReload(
                App.Services.GetRequiredService<CharacterGalleryViewModel>().Cards, current,
                ShowCard,
                UpdateNavigationButtons,
                () => { if (Frame.CanGoBack) Frame.GoBack(); });
        });
    }

    private void ShowCard(CharacterCard card)
    {
        var metadataCts = PageCancellation.Restart(ref _metadataCts);
        ViewModel.Card = card;
        LoadAnnotationInto(card);
        var bitmap = new BitmapImage { DecodePixelWidth = Math.Min(card.Width, 1920) };
        bitmap.UriSource = card.FileUri;
        PreviewImage.Source = bitmap;
        UpdateNavigationButtons();
        LoadMetadataAsync(card, metadataCts.Token).Observe(
            App.Services.GetRequiredService<IAppLogger>(),
            "CharacterDetail.LoadMetadata");
    }

    private async Task LoadMetadataAsync(CharacterCard card, CancellationToken cancellationToken)
    {
        ViewModel.MetadataLoaded = false;
        var meta = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = CharacterCardParser.TryParse(card.FilePath);
            cancellationToken.ThrowIfCancellationRequested();
            return parsed;
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(ViewModel.Card, card)) return;

        meta ??= new CharacterMetadata(null, null, null, -1, GameVersion.Unknown, false);
        ViewModel.FullName = string.IsNullOrWhiteSpace(meta.FullName)
            ? ResLoader.GetString("Common_Unknown")
            : meta.FullName;
        ViewModel.Nickname = meta.Nickname ?? string.Empty;
        ViewModel.SexDisplay = meta.Sex switch
        {
            CharacterMetadata.SexMale => ResLoader.GetString("Common_Male"),
            CharacterMetadata.SexFemale => ResLoader.GetString("Common_Female"),
            _ => ResLoader.GetString("Common_Unknown")
        };
        ViewModel.GameDisplay = meta.Game switch
        {
            GameVersion.Koikatsu => "Koikatsu",
            GameVersion.KoikatsuSunshine => "Koikatsu Sunshine",
            _ => ResLoader.GetString("Common_Unknown")
        };
        ViewModel.MadevilDisplay = meta.IsMadevil
            ? ResLoader.GetString("Common_Yes")
            : ResLoader.GetString("Common_No");
        ViewModel.MetadataLoaded = true;

        LoadVersions(
            card,
            App.Services.GetRequiredService<CharacterGalleryViewModel>().GetVersionKey(card)
                ?? meta.FullName);
    }

    private void LoadVersions(CharacterCard card, string fullName)
    {
        var versions = App.Services.GetRequiredService<CharacterGalleryViewModel>().GetVersions(fullName);
        if (versions != null && versions.Count > 1)
        {
            ViewModel.Versions = new System.Collections.ObjectModel.ObservableCollection<CharacterCard>(versions);
            ViewModel.HasMultipleVersions = true;
            ViewModel.TotalVersions = versions.Count;
            ViewModel.VersionIndex = versions.IndexOf(card) + 1;
            foreach (var v in versions)
                App.Services.GetRequiredService<CharacterGalleryViewModel>().RequestThumbnail(v);
        }
        else
        {
            ViewModel.Versions = null;
            ViewModel.HasMultipleVersions = false;
            ViewModel.VersionIndex = 0;
            ViewModel.TotalVersions = 0;
        }
    }

    // ── Annotation ────────────────────────────────────────────────

    /// <summary>
    /// True while the annotation controls are being filled in from a card, so
    /// the change events they raise are not written back as user edits.
    /// </summary>
    private bool _suppressAnnotationWrites;

    /// <summary>Whether the note box has focus, so arrow keys must not change card.</summary>
    private bool IsAnnotating => VersionNoteBox.FocusState != FocusState.Unfocused;

    private void LoadAnnotationInto(CharacterCard card)
    {
        _suppressAnnotationWrites = true;
        try
        {
            VersionKindBox.SelectedIndex = (int)card.VersionKind;
            VersionSupersededBox.IsChecked = card.IsSuperseded;
            VersionNoteBox.Text = card.VersionNote ?? string.Empty;
            ViewModel.GroupOverride = card.CharacterGroupKey;
        }
        finally
        {
            _suppressAnnotationWrites = false;
        }
    }

    private void VersionKind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAnnotationWrites || ViewModel.Card is not { } card) return;
        if (VersionKindBox.SelectedIndex < 0) return;

        SaveAnnotation(
            card,
            (CharacterVersionKind)VersionKindBox.SelectedIndex,
            VersionSupersededBox.IsChecked == true,
            VersionNoteBox.Text);
    }

    private void VersionSuperseded_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressAnnotationWrites || ViewModel.Card is not { } card) return;

        SaveAnnotation(card, card.VersionKind, VersionSupersededBox.IsChecked == true, VersionNoteBox.Text);
    }

    private void VersionNote_LostFocus(object sender, RoutedEventArgs e) => SaveNoteIfChanged();

    private void VersionNote_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;

        SaveNoteIfChanged();
        e.Handled = true;
    }

    /// <summary>
    /// Notes are saved on commit rather than per keystroke: each save is an
    /// atomic file write plus a re-rank of the character's cards.
    /// </summary>
    private void SaveNoteIfChanged()
    {
        if (_suppressAnnotationWrites || ViewModel.Card is not { } card) return;

        var note = VersionNoteBox.Text.Trim();
        if (string.Equals(note, card.VersionNote ?? string.Empty, StringComparison.Ordinal)) return;

        SaveAnnotation(card, card.VersionKind, card.IsSuperseded, note);
    }

    private void SaveAnnotation(
        CharacterCard card,
        CharacterVersionKind kind,
        bool superseded,
        string? note)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "CharacterDetail.Annotate", async () =>
            await App.Services.GetRequiredService<CharacterGalleryViewModel>()
                .ApplyAnnotationAsync(card, kind, superseded, note, card.CharacterGroupKey));

    private void AssignGroup_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "CharacterDetail.AssignGroup", async () =>
        {
            if (ViewModel.Card is not { } card) return;

            var gallery = App.Services.GetRequiredService<CharacterGalleryViewModel>();
            var chosen = await KoikatsuSceneGallery.Controls.CharacterGroupPicker.PickAsync(
                XamlRoot,
                gallery.GetVersionGroupKeys(card.Author),
                gallery.GetVersionKey(card));
            if (chosen is null) return;

            // The picker returns an empty string to mean "use the card's own name".
            var groupKey = chosen.Length == 0 ? null : chosen;
            await gallery.ApplyAnnotationAsync(
                card, card.VersionKind, card.IsSuperseded, card.VersionNote, groupKey);
            ViewModel.GroupOverride = card.CharacterGroupKey;
            LoadVersions(card, gallery.GetVersionKey(card) ?? ViewModel.FullName);
        });

    // ── Removing a version ────────────────────────────────────────

    private void RevealVersion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CharacterCard card })
            return;

        UiEventGuard.Run(
            App.Services.GetRequiredService<IAppLogger>(),
            "CharacterDetail.RevealVersion",
            async () =>
            {
                var folder = Path.GetDirectoryName(card.FilePath);
                if (folder is null)
                    return;

                var options = new Windows.System.FolderLauncherOptions();
                try
                {
                    options.ItemsToSelect.Add(
                        await Windows.Storage.StorageFile.GetFileFromPathAsync(card.FilePath));
                }
                catch (Exception)
                {
                    // Opening the folder without a selection is still useful.
                }

                await Windows.System.Launcher.LaunchFolderPathAsync(folder, options);
            });
    }

    /// <summary>
    /// Removes one of a character versions from disk, via the recycle bin.
    /// </summary>
    /// <remarks>
    /// The gallery finds out from its file watcher, but only after a debounce,
    /// so the version list is rebuilt here against what is actually on disk.
    /// Otherwise the card the user just deleted stays in the list.
    /// </remarks>
    private void DeleteVersion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CharacterCard card })
            return;

        UiEventGuard.Run(
            App.Services.GetRequiredService<IAppLogger>(),
            "CharacterDetail.DeleteVersion",
            () => DeleteVersionAsync(card));
    }

    private async Task DeleteVersionAsync(CharacterCard card)
    {
        var remaining = Math.Max(0, (ViewModel.Versions?.Count ?? 1) - 1);
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString("Detail_DeleteVersion_Title"),
            Content = new TextBlock
            {
                Text = string.Format(
                    ResLoader.GetString("Detail_DeleteVersion_Body"),
                    card.FileName,
                    remaining),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = ResLoader.GetString("Detail_DeleteVersion_Confirm"),
            CloseButtonText = ResLoader.GetString("LocalSources_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            return;

        var path = card.FilePath;
        if (!await Task.Run(() => RecycleBin.TryDelete(path)).ConfigureAwait(true))
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = ResLoader.GetString("Detail_DeleteVersion_FailedTitle"),
                Content = new TextBlock
                {
                    Text = string.Format(
                        ResLoader.GetString("Detail_DeleteVersion_Failed"),
                        card.FileName),
                    TextWrapping = TextWrapping.Wrap,
                },
                CloseButtonText = ResLoader.GetString("LocalSources_Edit_Close"),
            }.ShowAsync();
            return;
        }

        var gallery = App.Services.GetRequiredService<CharacterGalleryViewModel>();
        var survivors = (gallery.GetVersions(gallery.GetVersionKey(card) ?? ViewModel.FullName)
                ?? [])
            .Where(version => !string.Equals(
                version.FilePath, path, StringComparison.OrdinalIgnoreCase))
            .Where(version => File.Exists(version.FilePath))
            .ToArray();

        if (survivors.Length == 0)
        {
            GoBack_Click(this, new RoutedEventArgs());
            return;
        }

        var shown = string.Equals(ViewModel.Card?.FilePath, path, StringComparison.OrdinalIgnoreCase)
            ? survivors[0]
            : ViewModel.Card!;
        ShowCard(shown);
        ViewModel.Versions = new System.Collections.ObjectModel.ObservableCollection<CharacterCard>(survivors);
        ViewModel.HasMultipleVersions = survivors.Length > 1;
        ViewModel.TotalVersions = survivors.Length;
        ViewModel.VersionIndex = Array.IndexOf(survivors, shown) + 1;
    }

    private void VersionItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CharacterCard card && !ReferenceEquals(card, ViewModel.Card))
            ShowCard(card);
    }

    public static string FormatTimestamp(CharacterCard card) =>
        card.FileTimestamp.ToString("yyyy-MM-dd HH:mm:ss");

    private void UpdateNavigationButtons()
    {
        if (_viewer.Update()) return;
        var (hasPrev, hasNext) = DetailNavigationHelper.GetNavigationState(
            GetScopedCards(), App.Services.GetRequiredService<CharacterGalleryViewModel>().CardsView, ViewModel.Card);
        PrevButton.IsEnabled = hasPrev;
        NextButton.IsEnabled = hasNext;
    }

    private void Navigate(int direction)
    {
        if (_viewer.Navigate(direction)) return;
        var next = DetailNavigationHelper.Navigate(
            GetScopedCards(), App.Services.GetRequiredService<CharacterGalleryViewModel>().CardsView, ViewModel.Card, direction);
        if (next != null) ShowCard(next);
    }

    private void GoBack_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
    private void PrevButton_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewer.Navigate(0, true)) return;
        var card = DetailNavigationHelper.RandomCard(
            GetScopedCards(), App.Services.GetRequiredService<CharacterGalleryViewModel>().CardsView, ViewModel.Card);
        if (card != null) ShowCard(card);
    }

    private List<CharacterCard>? GetScopedCards() => _authorScope is not { } author
        ? null
        : App.Services.GetRequiredService<CharacterGalleryViewModel>().CardsView
            .OfType<CharacterCard>()
            .Where(card => card.Author?.Key == author)
            .ToList();

    private void PreviousCard_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewerChrome.IsEditing(XamlRoot) || KoikatsuSceneGallery.Controls.MetadataPanel.ContainsFocus(XamlRoot) || IsAnnotating) return;
        Navigate(-1);
        args.Handled = true;
    }

    private void NextCard_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewerChrome.IsEditing(XamlRoot) || KoikatsuSceneGallery.Controls.MetadataPanel.ContainsFocus(XamlRoot) || IsAnnotating) return;
        Navigate(1);
        args.Handled = true;
    }

    private void PixivButton_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "CharacterDetail.OpenPixiv", async () =>
        {
            if (ViewModel.PixivUrl is { } url)
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        });

    private void BepisDbButton_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "CharacterDetail.OpenBepisDb", async () =>
        {
            if (ViewModel.BepisDbUrl is { } url)
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        });

    private void PreviewImage_DragStarting(UIElement sender, DragStartingEventArgs e)
        => DetailNavigationHelper.HandleDragStartingAsync(ViewModel.Card, e)
            .Observe(App.Services.GetRequiredService<IAppLogger>(), "CharacterDetail.PrepareDrag");
}

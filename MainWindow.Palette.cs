using KoikatsuSceneGallery.Controls;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Pages;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SceneGallery.PluginSdk;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace KoikatsuSceneGallery;

/// <summary>What the Ctrl+K palette can find, and what picking each one does.</summary>
public sealed partial class MainWindow
{
    private void InitializePalette()
    {
        Palette.Source = BuildPaletteCandidates;
        var open = new KeyboardAccelerator { Key = VirtualKey.K, Modifiers = VirtualKeyModifiers.Control };
        open.Invoked += (_, args) =>
        {
            Palette.Open();
            args.Handled = true;
        };
        RootLayout.KeyboardAccelerators.Add(open);
    }

    private void PaletteButton_Click(object sender, RoutedEventArgs e) => Palette.Open();

    private IReadOnlyList<PaletteCandidate<PaletteEntry>> BuildPaletteCandidates()
    {
        var list = new List<PaletteCandidate<PaletteEntry>>();
        AddPageCommands(list);
        AddActionCommands(list);
        AddSettings(list);
        AddAuthors(list);
        AddCards(list);
        return list;
    }

    private static PaletteCandidate<PaletteEntry> Candidate(
        PaletteCategory category, string glyph, string title, string detail, string label,
        Action invoke, params string[] keywords)
        => new(title, keywords, category, new PaletteEntry(glyph, title, detail, label, invoke));

    /// <summary>Every page in the pane that is currently offered, plus the library's three kinds.</summary>
    private void AddPageCommands(List<PaletteCandidate<PaletteEntry>> list)
    {
        var page = UiText.Get("Palette_CategoryPage");
        var library = UiText.Get("Nav_Library.Content");
        foreach (var (item, glyph, english) in new[]
        {
            (ScenesSelectorItem, "", "library scenes studio"),
            (CharactersSelectorItem, "", "library characters chara"),
            (CoordinatesSelectorItem, "", "library coordinates outfits clothes"),
        })
        {
            if (item.Visibility != Visibility.Visible || LibraryNavItem.Visibility != Visibility.Visible) continue;
            var target = item;
            list.Add(Candidate(PaletteCategory.Command, glyph, item.Text, library, page,
                () => OpenLibrary(target), english, library));
        }

        foreach (var (item, glyph, english) in new[]
        {
            (HomeNavItem, "\uE80F", "home start shelves queue favourites"),
            (DiscoveryNavItem, "", "random discovery shuffle explore"),
            (RatingNavItem, "", "rating rate explore"),
            (AuthorsNavItem, "", "authors artists creators"),
            (TagsNavItem, "", "tags cloud"),
            (LocalSourcesNavItem, "", "local sources collection"),
            (ImportNavItem, "", "import add files"),
            (UsageNavItem, "", "usage history log record"),
        })
        {
            // Wave 16 took discovery, rating, tags, local sources and usage out
            // of the pane; the palette still offers every page there is.
            var tag = item.Tag as string ?? "";
            if (tag == "tags" && !TagsOffered) continue;
            if (tag is "home" or "authors" or "import" && item.Visibility != Visibility.Visible) continue;
            var target = item;
            var title = target == UsageNavItem ? UiText.Get("Nav_Usage_Label.Text") : item.Content as string ?? "";
            list.Add(Candidate(PaletteCategory.Command, glyph, title, "", page, () => NavigateToSection(tag), english));
        }

        var settings = UiText.Get("Palette_Settings");
        list.Add(Candidate(PaletteCategory.Command, "", settings, "", page,
            () => OpenSettings(new SettingsTarget(SettingsSearchIndex.General)), "settings options preferences"));
        for (var tab = 0; tab < SettingsSearchIndex.TabKeys.Count; tab++)
        {
            var target = new SettingsTarget(tab);
            var tabName = UiText.Get(SettingsSearchIndex.TabKeys[tab]);
            list.Add(Candidate(PaletteCategory.Command, "", $"{settings} › {tabName}", "", page,
                () => OpenSettings(target), "settings"));
        }
    }

    private void AddActionCommands(List<PaletteCandidate<PaletteEntry>> list)
    {
        var command = UiText.Get("Palette_CategoryCommand");
        var settings = App.Services.GetRequiredService<SettingsViewModel>();
        var recording = settings.RecordModeEnabled;
        list.Add(Candidate(PaletteCategory.Command, "",
            UiText.Get(recording ? "Palette_RecordOff" : "Palette_RecordOn"), "", command,
            () =>
            {
                settings.RecordModeEnabled = !recording;
                AppToast.Show(UiText.Get(recording ? "Record_NavToggleOff" : "Record_NavToggleOn"),
                    InfoBarSeverity.Success, key: "record-mode");
            },
            "record mode usage"));

        if (OriginSelectorBar.Visibility != Visibility.Visible && OriginSelectorPicker.Visibility != Visibility.Visible)
            return;
        var current = settings.EffectiveBrowseOrigin.ToConfigValue();
        foreach (var option in OriginSelectorMenu.Items.OfType<RadioMenuFlyoutItem>())
        {
            if (option.Tag is not string tag || tag == current) continue;
            list.Add(Candidate(PaletteCategory.Command, "",
                UiText.Format("Palette_SwitchPlatform", option.Text), "", command,
                () => settings.BrowseOrigin = CardOriginSelection.Parse(tag),
                "platform switch origin", option.Text));
        }
    }

    private void AddSettings(List<PaletteCandidate<PaletteEntry>> list)
    {
        var category = UiText.Get("Palette_CategorySetting");
        var settings = UiText.Get("Palette_Settings");
        foreach (var entry in SettingsSearchIndex.Entries)
        {
            var label = UiText.Get(entry.LabelKey);
            var tabName = UiText.Get(SettingsSearchIndex.TabKeys[entry.Tab]);
            var description = entry.DescriptionKey is { } key ? UiText.Get(key) : "";
            // A missing description comes back as its own key; that is not a keyword.
            if (description == entry.DescriptionKey) description = "";
            var target = new SettingsTarget(entry.Tab, label);
            list.Add(Candidate(PaletteCategory.Setting, "", label, $"{settings} › {tabName}", category,
                () => OpenSettings(target), description));
        }
    }

    private void AddAuthors(List<PaletteCandidate<PaletteEntry>> list)
    {
        var category = UiText.Get("Palette_CategoryAuthor");
        foreach (var tab in App.Services.GetRequiredService<AuthorsViewModel>().ProviderTabs)
        {
            foreach (var summary in tab.Authors)
            {
                var target = summary;
                list.Add(Candidate(PaletteCategory.Author, "", summary.Display.Name, tab.DisplayName, category,
                    () => NavFrame.Navigate(typeof(AuthorDetailPage), new AuthorDetailNavigationParameter(target)),
                    summary.Display.Key.Id, tab.ProviderId));
            }
        }
    }

    private void AddCards(List<PaletteCandidate<PaletteEntry>> list)
    {
        var category = UiText.Get("Palette_CategoryCard");
        foreach (var library in App.Services.GetRequiredService<LibraryRegistry>().All)
        {
            var glyph = library.Kind switch
            {
                LibraryKind.Characters => "",
                LibraryKind.Coordinates => "",
                _ => "",
            };
            foreach (var card in library.Cards)
            {
                var target = card;
                var author = (card as IAuthorOwner)?.Author?.Name ?? "";
                var detail = author.Length == 0 ? library.Title : $"{library.Title} · {author}";
                list.Add(Candidate(PaletteCategory.Card, glyph, card.FileName, detail, category,
                    () => BrowseContexts.From(target).Open(NavFrame, target), author));
            }
        }
    }

    private void OpenLibrary(SelectorBarItem item)
    {
        if (!ReferenceEquals(NavView.SelectedItem, LibraryNavItem)) NavView.SelectedItem = LibraryNavItem;
        LibrarySelectorBar.SelectedItem = item;
        NavigateToSelectedLibraryPage(replaceCurrentLibraryPage: true);
    }

    private void SelectNavItem(NavigationViewItem item)
    {
        // Selecting the entry that is already selected raises nothing, so the
        // navigation has to be asked for directly - the same case ItemInvoked
        // covers for a click.
        if (ReferenceEquals(NavView.SelectedItem, item)) NavigateToNavItem(item);
        else NavView.SelectedItem = item;
    }

    private void OpenSettings(SettingsTarget target)
    {
        if (NavFrame.Content is SettingsPage page) page.ShowTarget(target);
        else NavFrame.Navigate(typeof(SettingsPage), target);
    }
}

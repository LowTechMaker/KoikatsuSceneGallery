using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Pages;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// A tile's right-click menu, built when it opens so it can offer only what
/// applies to that tile: rating is for scene cards, "go to author" needs an
/// author, and a group acts on every card in it.
/// </summary>
/// <remarks>
/// The menu used to hold a single item, "open file location". Rating a card,
/// copying it or finding its author all meant opening the card first - or, for
/// rating, going to a separate page.
/// </remarks>
public sealed partial class LibraryBrowser
{
    private static readonly string[] RatingLabels = ["G", "R-18", "R-18G"];

    private void CardMenu_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout menu || menu.Target?.DataContext is not GalleryEntry entry) return;
        menu.Items.Clear();
        var count = entry.Members.Count;

        menu.Items.Add(Item(entry.IsGroup ? "CardMenu_OpenGroup" : "CardMenu_Open", "", () =>
        {
            OpenEntry(entry);
            return Task.CompletedTask;
        }));

        var scenes = entry.Members.OfType<SceneCard>().ToArray();
        if (scenes.Length > 0)
        {
            var rating = new MenuFlyoutSubItem
            {
                Text = count > 1 ? UiText.Format("CardMenu_RateMany", scenes.Length) : UiText.Get("CardMenu_Rate"),
                Icon = new FontIcon { Glyph = "" },
            };
            for (var i = 0; i < RatingLabels.Length; i++)
            {
                var value = i;
                rating.Items.Add(new MenuFlyoutItem
                {
                    Text = RatingLabels[i],
                    Command = new RelayAction(() => RateAsync(scenes, value)),
                });
            }

            rating.Items.Add(new MenuFlyoutSeparator());
            rating.Items.Add(new MenuFlyoutItem
            {
                Text = UiText.Get("CardMenu_RateClear"),
                Command = new RelayAction(() => RateAsync(scenes, null)),
            });
            menu.Items.Add(rating);
        }

        var paths = entry.Members.Select(card => card.FilePath).ToArray();
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(StateItem(entry.Card.FilePath, paths, CardFlag.Queued, "\uE710", "CardMenu_Queue"));
        menu.Items.Add(StateItem(entry.Card.FilePath, paths, CardFlag.Liked, "\uEB51", "CardMenu_Like"));
        menu.Items.Add(StateItem(entry.Card.FilePath, paths, CardFlag.Hidden, "\uED1A", "CardMenu_Hide"));

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(count > 1 ? "CardMenu_CopyFiles" : "CardMenu_CopyFile", "",
            () => CardFileActions.CopyFilesAsync([.. entry.Members.Select(card => card.FilePath)], "Browser.CopyFile"), count));
        if (count == 1)
            menu.Items.Add(Item("CardMenu_CopyPath", "",
                () => { CardFileActions.CopyPath(entry.Card.FilePath, "Browser.CopyFilePath"); return Task.CompletedTask; }));
        menu.Items.Add(Item("Gallery_OpenFileLocation.Text", "", () => CardFileActions.OpenFolderAsync(entry.Card.FilePath)));

        if (entry.Author is { } author && FindAuthor(author) is { } summary)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item("CardMenu_GoToAuthor", "", () =>
            {
                _frame?.Navigate(typeof(AuthorDetailPage), new AuthorDetailNavigationParameter(summary));
                return Task.CompletedTask;
            }, author.Name));
        }
    }

    private MenuFlyoutItem Item(string key, string glyph, Func<Task> run, params object[] args) => new()
    {
        Text = args.Length == 0 ? UiText.Get(key) : UiText.Format(key, args),
        Icon = new FontIcon { Glyph = glyph },
        Command = new RelayAction(run),
    };

    private static MenuFlyoutItem StateItem(string firstCardPath, IReadOnlyList<string> paths, CardFlag flag, string glyph, string automationId)
    {
        var item = new MenuFlyoutItem
        {
            Text = CardStateActions.Label(firstCardPath, flag),
            Icon = new FontIcon { Glyph = glyph },
            Command = new RelayAction(() => CardStateActions.ToggleAsync(paths, flag)),
        };
        AutomationProperties.SetAutomationId(item, automationId);
        return item;
    }

    private static AuthorSummary? FindAuthor(AuthorDisplay author) =>
        App.Services.GetRequiredService<AuthorInfoService>().GetSummaries()
            .FirstOrDefault(summary => summary.Display.Key == author.Key);

    /// <summary>
    /// Sets (or clears) the user's own rating on every scene card in the tile.
    /// Hashing reads each file, so it runs off the UI thread; the badges update
    /// through the gallery's own rating-changed handling.
    /// </summary>
    private async Task RateAsync(IReadOnlyList<SceneCard> cards, int? rating)
    {
        var store = App.Services.GetRequiredService<UserCardRatingStore>();
        var roots = _settings.FolderPaths.ToArray();
        var hashed = await Task.Run(() =>
        {
            store.EnsureLoaded(roots);
            return cards.Select(card => (card, hash: CardPayloadHash.TryCompute(card.FilePath))).ToArray();
        });

        var failed = 0;
        foreach (var (card, hash) in hashed)
        {
            var ok = hash is not null && (rating is { } value
                ? await store.SetAsync(card.FilePath, hash, value)
                : await store.ClearAsync(card.FilePath, hash));
            if (!ok) failed++;
        }

        if (failed > 0)
        {
            AppToast.Show(UiText.Get("Rating_SaveFailed"), InfoBarSeverity.Error, key: "rating-save");
            return;
        }

        var label = rating is { } chosen ? RatingLabels[chosen] : UiText.Get("CardMenu_RateClear");
        var message = cards.Count == 1
            ? UiText.Format("CardMenu_RatedOne", label)
            : UiText.Format("CardMenu_RatedMany", cards.Count, label);
        // With the platform as the rating source the badge on the tile does not
        // change, which reads as "it did not save" unless something says why.
        if (!_settings.UseUserRatings) message += " " + UiText.Get("CardMenu_RatedPlatformHint");
        AppToast.Show(message, InfoBarSeverity.Success, key: "rating-save");
    }

    /// <summary>A menu item's command: runs the action under the UI event guard.</summary>
    private sealed class RelayAction(Func<Task> run) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) =>
            UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Browser.CardMenu", run);
    }
}

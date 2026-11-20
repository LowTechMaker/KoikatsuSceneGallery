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
/// A card's right-click menu, built when it opens so it can offer only what
/// applies: rating is for scene cards, "go to author" needs an author, and a
/// group acts on every card in it. The gallery's tiles and the home page's
/// share it, so the same card offers the same things everywhere.
/// </summary>
/// <remarks>
/// The menu used to hold a single item, "open file location". Rating a card,
/// copying it or finding its author all meant opening the card first - or, for
/// rating, going to a separate page.
/// </remarks>
internal static class CardMenu
{
    /// <summary>Fills <paramref name="menu"/> for <paramref name="members"/> (one card, or a group with its cover first).</summary>
    /// <param name="open">Opens the card or group, as a click on the tile would.</param>
    /// <param name="frame">The frame "go to author" navigates; without one the item is left out.</param>
    public static void Fill(MenuFlyout menu, IReadOnlyList<CardBase> members, Action open, Frame? frame)
    {
        menu.Items.Clear();
        if (members.Count == 0) return;
        var count = members.Count;
        var first = members[0];

        menu.Items.Add(Item(count > 1 ? "CardMenu_OpenGroup" : "CardMenu_Open", Glyphs.Open, () =>
        {
            open();
            return Task.CompletedTask;
        }));

        var scenes = members.OfType<SceneCard>().ToArray();
        if (scenes.Length > 0)
        {
            var rating = new MenuFlyoutSubItem
            {
                Text = count > 1 ? UiText.Format("CardMenu_RateMany", scenes.Length) : UiText.Get("CardMenu_Rate"),
                Icon = new FontIcon { Glyph = Glyphs.Rating },
            };
            for (var i = 0; i < CardRatingActions.Labels.Length; i++)
            {
                var value = i;
                rating.Items.Add(new MenuFlyoutItem
                {
                    Text = CardRatingActions.Labels[i],
                    Command = new RelayAction(() => CardRatingActions.RateManyAsync(scenes, value)),
                });
            }

            rating.Items.Add(new MenuFlyoutSeparator());
            rating.Items.Add(new MenuFlyoutItem
            {
                Text = UiText.Get("CardMenu_RateClear"),
                Command = new RelayAction(() => CardRatingActions.RateManyAsync(scenes, null)),
            });
            menu.Items.Add(rating);
        }

        var paths = members.Select(card => card.FilePath).ToArray();
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(StateItem(first.FilePath, paths, CardFlag.Queued, Glyphs.Queue, "CardMenu_Queue"));
        menu.Items.Add(StateItem(first.FilePath, paths, CardFlag.Liked, Glyphs.Like, "CardMenu_Like"));
        menu.Items.Add(StateItem(first.FilePath, paths, CardFlag.Hidden, Glyphs.Hide, "CardMenu_Hide"));

        if (count == 1 && CardOrigin.NeedsSource(first))
        {
            var addSource = Item("CardMenu_AddSource", Glyphs.CopyLink, () => Controls.ReidentifyDialog.ShowAsync(menu.XamlRoot, first.FilePath));
            AutomationProperties.SetAutomationId(addSource, "CardMenu_AddSource");
            menu.Items.Add(addSource);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(count > 1 ? "CardMenu_CopyFiles" : "CardMenu_CopyFile", Glyphs.CopyFile,
            () => CardFileActions.CopyFilesAsync([.. members.Select(card => card.FilePath)], "Browser.CopyFile"), count));
        if (count == 1)
            menu.Items.Add(Item("CardMenu_CopyPath", Glyphs.CopyLink,
                () => { CardFileActions.CopyPath(first.FilePath, "Browser.CopyFilePath"); return Task.CompletedTask; }));
        menu.Items.Add(Item("Gallery_OpenFileLocation.Text", Glyphs.OpenFolder, () => CardFileActions.OpenFolderAsync(first.FilePath)));

        if (frame is not null && (first as IAuthorOwner)?.Author is { } author && FindAuthor(author) is { } summary)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item("CardMenu_GoToAuthor", Glyphs.Author, () =>
            {
                frame.Navigate(typeof(AuthorDetailPage), new AuthorDetailNavigationParameter(summary));
                return Task.CompletedTask;
            }, author.Name));
        }
    }

    private static MenuFlyoutItem Item(string key, string glyph, Func<Task> run, params object[] args) => new()
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

    internal static AuthorSummary? FindAuthor(AuthorDisplay author) =>
        App.Services.GetRequiredService<AuthorInfoService>().GetSummaries()
            .FirstOrDefault(summary => summary.Display.Key == author.Key);

    /// <summary>A menu item's command: runs the action under the UI event guard.</summary>
    private sealed class RelayAction(Func<Task> run) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) =>
            UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Browser.CardMenu", run);
    }
}

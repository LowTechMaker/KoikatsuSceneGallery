using KoikatsuSceneGallery.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// Asks which character a card should be filed under.
/// </summary>
/// <remarks>
/// Needed because a character has no stable identity: the version index groups
/// by name, so a what-if version that changes the name lands in its own group.
/// This is how the user puts it back.
/// </remarks>
internal static class CharacterGroupPicker
{
    private static readonly ResourceLoader ResLoader = new();

    private sealed record Choice(string Key, int Count, bool SameAuthor)
    {
        public string Display => UiText.Format("Detail_GroupEntry", Key, Count);
    }

    /// <summary>
    /// The chosen character's key, an empty string to fall back to the card's
    /// own name, or null when the user cancelled.
    /// </summary>
    /// <remarks>
    /// Only the card's own author's characters are listed to begin with. The
    /// whole index would be hundreds of unrelated characters, and picking one
    /// by accident would file the card under someone else's work.
    /// </remarks>
    public static async Task<string?> PickAsync(
        XamlRoot xamlRoot,
        IReadOnlyList<(string Key, int Count, bool SameAuthor)> groups,
        string? currentKey)
    {
        var all = groups
            .Where(group => !string.Equals(group.Key, currentKey, StringComparison.OrdinalIgnoreCase))
            .Select(group => new Choice(group.Key, group.Count, group.SameAuthor))
            .ToList();

        // With no author to scope by — or no characters under it — showing only
        // the author's own would show nothing at all.
        var canScope = all.Any(choice => choice.SameAuthor);

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            DisplayMemberPath = nameof(Choice.Display),
            MaxHeight = 320,
        };
        var search = new AutoSuggestBox
        {
            PlaceholderText = ResLoader.GetString("Detail_GroupDialog_SearchPlaceholder"),
            QueryIcon = new SymbolIcon(Symbol.Find),
        };
        var allAuthors = new CheckBox
        {
            Content = ResLoader.GetString("Detail_GroupDialog_AllAuthors"),
            IsChecked = !canScope,
            IsEnabled = canScope,
        };

        void Refresh()
        {
            var query = search.Text.Trim();
            // A library holds a few thousand characters at most, so filtering
            // the in-memory list beats any index.
            list.ItemsSource = all
                .Where(choice => allAuthors.IsChecked == true || choice.SameAuthor)
                .Where(choice => query.Length == 0
                                 || choice.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        search.TextChanged += (_, _) => Refresh();
        allAuthors.Checked += (_, _) => Refresh();
        allAuthors.Unchecked += (_, _) => Refresh();
        Refresh();

        var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
        panel.Children.Add(search);
        panel.Children.Add(allAuthors);
        panel.Children.Add(list);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = ResLoader.GetString("Detail_GroupDialog_Title"),
            Content = panel,
            // Secondary clears the override rather than choosing a character.
            SecondaryButtonText = ResLoader.GetString("Detail_GroupDialog_Clear"),
            CloseButtonText = ResLoader.GetString("Detail_GroupDialog_Cancel"),
        };

        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is Choice)
                dialog.Hide();
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
            return string.Empty;

        return list.SelectedItem is Choice choice ? choice.Key : null;
    }
}

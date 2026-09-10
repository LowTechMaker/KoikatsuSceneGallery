using KoikatsuSceneGallery.Controls;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Runs the edit of a local source end to end: gathers what the dialog needs,
/// applies what the user confirmed, and makes the change visible.
/// </summary>
/// <remarks>
/// Shared by the three entry points so the order of operations is decided
/// once. It matters: the avatar is written into the folders the source owns
/// now, and only then is the folder renamed, so the picture travels with the
/// move instead of being written to a path that no longer exists.
/// </remarks>
internal static class LocalSourceEditing
{
    /// <summary>How many of a source cards are offered as avatar candidates.</summary>
    private const int MaxCardCandidates = 240;

    private static readonly ResourceLoader ResLoader = new();

    /// <summary>
    /// Shows the editor for one local source and applies the result. True when
    /// something was changed.
    /// </summary>
    public static async Task<bool> RunAsync(XamlRoot xamlRoot, string? sourceId)
    {
        if (string.IsNullOrEmpty(sourceId))
            return false;

        var registry = App.Services.GetRequiredService<LocalSourceRegistry>();
        var source = registry.Find(sourceId);
        if (source is null)
            return false;

        var directories = registry.DirectoriesOf(sourceId);
        var cards = await Task.Run(() => CollectCards(directories)).ConfigureAwait(true);
        var edit = await LocalSourceEditor
            .EditAsync(xamlRoot, source, cards, CollectAuthorAvatars())
            .ConfigureAwait(true);
        if (edit is null)
            return false;

        if (edit.ClearAvatar)
            await registry.ClearAvatarAsync(sourceId).ConfigureAwait(true);
        else if (edit.AvatarImagePath is { } image)
            await registry.SetAvatarAsync(sourceId, image).ConfigureAwait(true);

        if (!string.Equals(edit.DisplayName, source.DisplayName, StringComparison.Ordinal))
        {
            var renamed = await registry
                .RenameAsync(sourceId, edit.DisplayName)
                .ConfigureAwait(true);
            if (renamed.FoldersLeftBehind.Count > 0)
                await ReportLeftBehindAsync(xamlRoot, renamed).ConfigureAwait(true);
        }

        // The name and picture the rest of the app shows come from the author
        // record, which caches them; a forced refresh re-reads the disk.
        await App.Services
            .GetRequiredService<AuthorInfoService>()
            .RefreshAuthorAsync(new AuthorKey(LocalSourceIdentity.ProviderId, sourceId))
            .ConfigureAwait(true);

        return true;
    }

    /// <summary>
    /// Card files under the folders a source owns, newest first. Runs off the
    /// UI thread: this walks the source folders.
    /// </summary>
    private static List<string> CollectCards(IReadOnlyList<string> directories)
    {
        var files = new List<FileInfo>();
        foreach (var directory in directories)
        {
            try
            {
                files.AddRange(new DirectoryInfo(directory)
                    .EnumerateFiles("*.png", SearchOption.AllDirectories));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be listed just offers no candidates.
            }
        }

        return [.. files
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaxCardCandidates)
            .Select(file => file.FullName)];
    }

    /// <summary>
    /// Avatars of the remote authors the library already knows, for a friend
    /// who also posts online. Local sources are excluded: their pictures are
    /// what this dialog sets.
    /// </summary>
    private static List<(string Name, string AvatarPath)> CollectAuthorAvatars()
        => [.. App.Services
            .GetRequiredService<AuthorInfoService>()
            .GetSummaries()
            .Where(summary => summary.Display is { HasAvatar: true, IsLocalSource: false })
            .Select(summary => (summary.Display.Name, AvatarPath: summary.Display.AvatarPath!))
            .DistinctBy(author => author.AvatarPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(author => author.Name, StringComparer.CurrentCulture)];

    private static async Task ReportLeftBehindAsync(
        XamlRoot xamlRoot,
        LocalSourceRenameResult renamed)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = ResLoader.GetString("LocalSources_Edit_FolderKeptTitle"),
            Content = new TextBlock
            {
                Text = string.Format(
                    ResLoader.GetString("LocalSources_Edit_FolderKeptBody"),
                    renamed.DisplayName,
                    string.Join(Environment.NewLine, renamed.FoldersLeftBehind)),
                TextWrapping = TextWrapping.Wrap,
            },
            CloseButtonText = ResLoader.GetString("LocalSources_Edit_Close"),
        };

        await dialog.ShowAsync();
    }
}

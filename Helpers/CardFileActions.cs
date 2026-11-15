using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// What can be done with a card's file from anywhere in the app: open its
/// folder, copy the file itself, copy its path. One implementation for the
/// gallery's right-click menu and the detail pages' action bar, so the two
/// cannot drift apart.
/// </summary>
internal static class CardFileActions
{
    public static async Task OpenFolderAsync(string filePath)
    {
        if (Path.GetDirectoryName(filePath) is { } folder)
            await Windows.System.Launcher.LaunchFolderPathAsync(folder);
    }

    /// <summary>Puts the files themselves on the clipboard, ready to paste into Explorer or the game's folder.</summary>
    public static async Task CopyFilesAsync(IReadOnlyList<string> paths, string operation)
    {
        var files = new List<IStorageItem>(paths.Count);
        foreach (var path in paths)
        {
            try { files.Add(await StorageFile.GetFileFromPathAsync(path)); }
            catch (Exception ex) { App.Services.GetRequiredService<IAppLogger>().LogError(operation, ex, path); }
        }

        if (files.Count == 0) return;
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetStorageItems(files);
        Clipboard.SetContent(package);
        // Copying cards out is exactly what record mode is for.
        App.Services.GetService<UsageRecordService>()?.Record(paths, operation);
        AppToast.Show(UiText.Format("CardMenu_FilesCopied", files.Count), InfoBarSeverity.Success, key: "clipboard");
    }

    public static void CopyPath(string filePath, string operation)
    {
        DetailNavigationHelper.CopyText(filePath);
        App.Services.GetService<UsageRecordService>()?.Record([filePath], operation);
    }

    /// <summary>A link copied counts as the card taken somewhere, like a path.</summary>
    public static void CopyLink(string url, string filePath, string operation)
    {
        DetailNavigationHelper.CopyText(url);
        App.Services.GetService<UsageRecordService>()?.Record([filePath], operation);
    }
}

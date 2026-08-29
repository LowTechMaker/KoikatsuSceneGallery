using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace KoikatsuSceneGallery.Helpers;

internal static class CardDragDropHelper
{
    public static bool ContainsStorageItems(DataPackageView dataView) =>
        dataView.Contains(StandardDataFormats.StorageItems);

    public static async Task<IReadOnlyList<string>> GetValidCardPathsAsync(
        DataPackageView dataView,
        FriendService friendService)
    {
        if (!ContainsStorageItems(dataView))
            return [];

        var storageItems = await dataView.GetStorageItemsAsync();
        var pngPaths = storageItems
            .OfType<StorageFile>()
            .Select(file => file.Path)
            .Where(path =>
                !string.IsNullOrWhiteSpace(path)
                && Path.GetExtension(path).Equals(
                    ".png",
                    StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return await Task.Run(() =>
            (IReadOnlyList<string>)pngPaths
                .Where(path =>
                    friendService.GetLinkedCardType(path) != CardType.NotACard)
                .ToList());
    }

    public static void SetDraggedFiles(
        DataPackage data,
        IEnumerable<string> filePaths,
        IAppLogger logger,
        string operation)
    {
        var paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
            return;

        data.RequestedOperation = DataPackageOperation.Copy;
        data.SetDataProvider(
            StandardDataFormats.StorageItems,
            async request =>
            {
                var deferral = request.GetDeferral();
                try
                {
                    var files = new List<IStorageItem>();
                    foreach (var path in paths)
                    {
                        try
                        {
                            files.Add(await StorageFile.GetFileFromPathAsync(path));
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(operation, ex, path);
                        }
                    }

                    request.SetData(files);
                }
                finally
                {
                    deferral.Complete();
                }
            });
    }
}

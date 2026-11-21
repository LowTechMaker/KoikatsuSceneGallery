using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>Drag library items out as real files. Paths resolve lazily so the grid stays responsive.</summary>
internal static class DragFilePayload
{
    public static void Attach(DragItemsStartingEventArgs e, string operation, IEnumerable<string> filePaths)
    {
        if (!Fill(e.Data, operation, filePaths)) e.Cancel = true;
    }

    /// <summary>
    /// The same for a single element dragged by itself (<see cref="UIElement.CanDrag"/>),
    /// such as a home page tile, so it is recorded like a gallery drag.
    /// </summary>
    public static void Attach(DragStartingEventArgs e, string operation, string filePath)
    {
        if (!Fill(e.Data, operation, [filePath])) e.Cancel = true;
    }

    private static bool Fill(DataPackage data, string operation, IEnumerable<string> filePaths)
    {
        var paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0) return false;
        // Every drag-out in the app funnels through here, so record mode needs
        // exactly this one hook rather than six.
        App.Services.GetService<UsageRecordService>()?.Record(paths, operation);
        // "Used" is recorded whether or not record mode is on: it is what fills
        // the home page's recent shelf and the tick on a tile.
        App.Services.GetService<CardStateService>()?.MarkUsed(paths);
        data.RequestedOperation = DataPackageOperation.Copy;
        InternalDrag.Mark(data);
        data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var files = new List<IStorageItem>(paths.Length);
                foreach (var path in paths)
                {
                    try { files.Add(await StorageFile.GetFileFromPathAsync(path)); }
                    catch (Exception ex)
                    {
                        App.Services.GetRequiredService<IAppLogger>().LogError(operation, ex, path);
                    }
                }
                request.SetData(files);
            }
            finally { deferral.Complete(); }
        });
        return true;
    }
}

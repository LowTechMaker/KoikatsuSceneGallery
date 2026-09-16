using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>Drag library items out as real files. Paths resolve lazily so the grid stays responsive.</summary>
internal static class DragFilePayload
{
    public static void Attach(DragItemsStartingEventArgs e, string operation, IEnumerable<string> filePaths)
    {
        var paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0) { e.Cancel = true; return; }
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
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
    }
}

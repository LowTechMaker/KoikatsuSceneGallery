using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public class SceneCardService : CardScanService<SceneCard>
{
    protected override IEnumerable<FileInfo> EnumerateCardFiles(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles("*.png", SearchOption.AllDirectories);

    protected override void ConfigureWatcher(FileSystemWatcher watcher) =>
        watcher.Filter = "*.png";

    protected override SceneCard? TryCreateCard(FileInfo info)
    {
        try
        {
            if (!info.Exists) return null;

            var (width, height) = PngHelper.ReadDimensions(info.FullName);
            return new SceneCard
            {
                FilePath = info.FullName,
                FileSize = info.Length,
                DateModified = info.LastWriteTime,
                Width = width,
                Height = height
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}

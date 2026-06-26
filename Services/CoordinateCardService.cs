using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public class CoordinateCardService : CardScanService<CoordinateCard>
{
    protected override IEnumerable<FileInfo> EnumerateCardFiles(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles("*.png", SearchOption.AllDirectories);

    protected override void ConfigureWatcher(FileSystemWatcher watcher) =>
        watcher.Filter = "*.png";

    protected override CoordinateCard? TryCreateCard(FileInfo info)
    {
        try
        {
            if (!info.Exists) return null;

            var (width, height) = PngHelper.ReadDimensions(info.FullName);
            return new CoordinateCard
            {
                FilePath = info.FullName,
                FileSize = info.Length,
                DateModified = info.LastWriteTime,
                DateCreated = info.CreationTime,
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

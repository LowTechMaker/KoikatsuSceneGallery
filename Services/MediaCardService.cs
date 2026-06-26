using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public class MediaCardService : CardScanService<MediaCard>
{
    private readonly string[] _extensions;
    private readonly bool _isVideo;

    public MediaCardService(string[] extensions, bool isVideo)
    {
        _extensions = extensions;
        _isVideo = isVideo;
    }

    protected override IEnumerable<FileInfo> EnumerateCardFiles(string folder) =>
        new DirectoryInfo(folder)
            .EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Where(f => _extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase));

    protected override void ConfigureWatcher(FileSystemWatcher watcher)
    {
        foreach (var ext in _extensions)
            watcher.Filters.Add($"*{ext}");
    }

    protected override MediaCard? TryCreateCard(FileInfo info)
    {
        try
        {
            if (!info.Exists) return null;

            return new MediaCard
            {
                FilePath = info.FullName,
                FileSize = info.Length,
                DateModified = info.LastWriteTime,
                Width = 0,
                Height = 0,
                IsVideo = _isVideo
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}

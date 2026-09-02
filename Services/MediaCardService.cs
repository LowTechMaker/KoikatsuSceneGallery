using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public class MediaCardService : CardScanService<MediaCard>
{
    private readonly string[] _extensions;

    public MediaCardService(string[] extensions)
    {
        _extensions = extensions;
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
                Height = 0
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}

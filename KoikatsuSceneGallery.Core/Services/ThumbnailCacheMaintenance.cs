namespace KoikatsuSceneGallery.Services;

public readonly record struct ThumbnailCacheUsage(long FileCount, long Bytes);
public readonly record struct ThumbnailCacheClearResult(long DeletedCount, long FailedCount);

/// <summary>Maintains the top-level JPEG files used by the thumbnail cache.</summary>
public static class ThumbnailCacheMaintenance
{
    public static ThumbnailCacheUsage Measure(string folder, CancellationToken cancellationToken = default)
    {
        long count = 0, bytes = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.jpg"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    bytes += new FileInfo(path).Length;
                    count++;
                }
                catch (FileNotFoundException) { } // A concurrent clear can remove a file.
            }
        }
        catch (DirectoryNotFoundException) { }
        return new(count, bytes);
    }

    public static ThumbnailCacheClearResult Clear(string folder,
        Action<Exception, string>? reportFailure = null, CancellationToken cancellationToken = default)
    {
        long deleted = 0, failed = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.jpg"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Delete(path);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failed++;
                    reportFailure?.Invoke(ex, path);
                }
            }
        }
        catch (DirectoryNotFoundException) { }
        return new(deleted, failed);
    }
}

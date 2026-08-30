using KoikatsuSceneGallery.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Bounds the app-owned strong references to decoded gallery thumbnails.
/// BitmapImage instances remain UI-affine, so every operation is confined to
/// the application's UI thread. Eviction only removes this cache's reference;
/// it never disposes or mutates an image currently used by a control.
/// </summary>
public sealed class ThumbnailMemoryCache
{
    // 96 images retains a generous working set for the current CacheLength=4
    // galleries while putting an explicit ceiling on model-owned references.
    public const int DefaultCapacity = 96;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly BoundedLruCache<string, BitmapImage> _images;

    public ThumbnailMemoryCache(DispatcherQueue dispatcherQueue, int capacity = DefaultCapacity)
    {
        _dispatcherQueue = dispatcherQueue;
        _images = new BoundedLruCache<string, BitmapImage>(
            capacity,
            StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _images.Count;

    public BitmapImage GetOrCreate(string thumbnailPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailPath);
        EnsureUiThread();

        return _images.GetOrAdd(
            thumbnailPath,
            static path => new BitmapImage(new Uri(path)) { DecodePixelWidth = 300 });
    }

    private void EnsureUiThread()
    {
        if (!_dispatcherQueue.HasThreadAccess)
        {
            throw new InvalidOperationException(
                "ThumbnailMemoryCache must be accessed on its owning UI thread.");
        }
    }
}

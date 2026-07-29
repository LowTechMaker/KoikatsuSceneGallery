using System.Security.Cryptography;
using System.Text;
using KoikatsuSceneGallery.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace KoikatsuSceneGallery.Services;

public class ThumbnailCacheService
{
    private const int ThumbnailWidth = 300;
    private volatile string _cacheFolder;
    private readonly IAppLogger _logger;

    public string CacheFolder => _cacheFolder;

    public static string DefaultCacheFolder =>
        Path.Combine(AppPaths.LocalFolder, "gallery_temp");

    public ThumbnailCacheService(IAppLogger logger, string? cacheFolder = null)
    {
        _logger = logger;
        _cacheFolder = string.IsNullOrWhiteSpace(cacheFolder) ? DefaultCacheFolder : cacheFolder;
        Directory.CreateDirectory(_cacheFolder);
    }

    public void SetCacheFolder(string path)
    {
        _cacheFolder = string.IsNullOrWhiteSpace(path) ? DefaultCacheFolder : path;
        Directory.CreateDirectory(_cacheFolder);
    }

    public string? TryGetCachedPath(SceneCard card) =>
        TryGetCachedPath(card.FilePath, card.DateModified);

    public string? TryGetCachedPath(string filePath, DateTime dateModified)
    {
        var folder = _cacheFolder;
        var cacheKey = ComputeCacheKey(filePath, dateModified);
        var cachePath = Path.Combine(folder, $"{cacheKey}.jpg");
        return GetUsableCachePath(cachePath);
    }

    public Task<string?> EnsureThumbnailAsync(
        SceneCard card,
        CancellationToken cancellationToken = default) =>
        EnsureThumbnailAsync(card.FilePath, card.DateModified, cancellationToken);

    public async Task<string?> EnsureThumbnailAsync(
        string filePath,
        DateTime dateModified,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = _cacheFolder;
            var cacheKey = ComputeCacheKey(filePath, dateModified);
            var cachePath = Path.Combine(folder, $"{cacheKey}.jpg");

            var existingCachePath = GetUsableCachePath(cachePath);
            if (existingCachePath is not null)
                return existingCachePath;

            var file = await StorageFile.GetFileFromPathAsync(filePath);
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            cancellationToken.ThrowIfCancellationRequested();

            var originalWidth = decoder.PixelWidth;
            var originalHeight = decoder.PixelHeight;
            var scale = (double)ThumbnailWidth / originalWidth;
            var scaledHeight = (uint)(originalHeight * scale);

            var transform = new BitmapTransform
            {
                ScaledWidth = ThumbnailWidth,
                ScaledHeight = scaledHeight,
                InterpolationMode = BitmapInterpolationMode.Linear
            };

            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);
            cancellationToken.ThrowIfCancellationRequested();

            return await WriteJpegAtomicallyAsync(
                folder,
                cacheKey,
                cachePath,
                ThumbnailWidth,
                scaledHeight,
                decoder.DpiX,
                decoder.DpiY,
                pixels.DetachPixelData(),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Thumbnail.GenerateImage", ex, filePath);
            return null;
        }
    }

    public async Task<string?> EnsureVideoThumbnailAsync(
        string filePath,
        DateTime dateModified,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = _cacheFolder;
            var cacheKey = ComputeCacheKey(filePath, dateModified);
            var cachePath = Path.Combine(folder, $"{cacheKey}.jpg");

            var existingCachePath = GetUsableCachePath(cachePath);
            if (existingCachePath is not null)
                return existingCachePath;

            var file = await StorageFile.GetFileFromPathAsync(filePath);
            cancellationToken.ThrowIfCancellationRequested();
            using var thumbnail = await file.GetThumbnailAsync(
                Windows.Storage.FileProperties.ThumbnailMode.SingleItem, ThumbnailWidth);
            if (thumbnail == null) return null;
            cancellationToken.ThrowIfCancellationRequested();

            var decoder = await BitmapDecoder.CreateAsync(thumbnail);

            var scale = (double)ThumbnailWidth / decoder.PixelWidth;
            var scaledHeight = (uint)(decoder.PixelHeight * scale);

            var transform = new BitmapTransform
            {
                ScaledWidth = ThumbnailWidth,
                ScaledHeight = scaledHeight,
                InterpolationMode = BitmapInterpolationMode.Linear
            };

            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);
            cancellationToken.ThrowIfCancellationRequested();

            return await WriteJpegAtomicallyAsync(
                folder,
                cacheKey,
                cachePath,
                ThumbnailWidth,
                scaledHeight,
                decoder.DpiX,
                decoder.DpiY,
                pixels.DetachPixelData(),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Thumbnail.GenerateVideo", ex, filePath);
            return null;
        }
    }

    public async Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        var folder = _cacheFolder;
        await Task.Run(() =>
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, "*.jpg"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { File.Delete(file); }
                catch (Exception ex) { _logger.LogError("Thumbnail.Delete", ex, file); }
            }
        }, cancellationToken);
    }

    private static string ComputeCacheKey(string filePath, DateTime dateModified)
    {
        var input = $"{filePath}|{dateModified.Ticks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16];
    }

    private string? GetUsableCachePath(string cachePath)
    {
        if (!File.Exists(cachePath))
            return null;
        if (JpegCacheFile.IsComplete(cachePath))
            return cachePath;

        TryDeleteFile("Thumbnail.DeleteInvalidCache", cachePath);
        return null;
    }

    private async Task<string?> WriteJpegAtomicallyAsync(
        string folder,
        string cacheKey,
        string cachePath,
        uint width,
        uint height,
        double dpiX,
        double dpiY,
        byte[] pixelData,
        CancellationToken cancellationToken)
    {
        var temporaryName = $"{cacheKey}.{Guid.NewGuid():N}.tmp";
        var temporaryPath = Path.Combine(folder, temporaryName);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cacheFolder = await StorageFolder.GetFolderFromPathAsync(folder);
            var outputFile = await cacheFolder.CreateFileAsync(
                temporaryName,
                CreationCollisionOption.FailIfExists);
            cancellationToken.ThrowIfCancellationRequested();

            using (var outputStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(
                    BitmapEncoder.JpegEncoderId,
                    outputStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    width,
                    height,
                    dpiX,
                    dpiY,
                    pixelData);

                await encoder.FlushAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, cachePath, overwrite: true);
            if (JpegCacheFile.IsComplete(cachePath))
                return cachePath;

            TryDeleteFile("Thumbnail.DeleteInvalidPublishedCache", cachePath);
            return null;
        }
        finally
        {
            TryDeleteFile("Thumbnail.DeleteTemporaryCache", temporaryPath);
        }
    }

    private void TryDeleteFile(string operation, string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(operation, ex, filePath);
        }
    }
}

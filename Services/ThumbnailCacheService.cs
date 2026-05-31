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
    private string _cacheFolder;

    public string CacheFolder => _cacheFolder;

    public static string DefaultCacheFolder =>
        Path.Combine(ApplicationData.Current.LocalFolder.Path, "gallery_temp");

    public ThumbnailCacheService(string? cacheFolder = null)
    {
        _cacheFolder = string.IsNullOrWhiteSpace(cacheFolder) ? DefaultCacheFolder : cacheFolder;
        Directory.CreateDirectory(_cacheFolder);
    }

    public void SetCacheFolder(string path)
    {
        _cacheFolder = string.IsNullOrWhiteSpace(path) ? DefaultCacheFolder : path;
        Directory.CreateDirectory(_cacheFolder);
    }

    public Task<string?> EnsureThumbnailAsync(SceneCard card) =>
        EnsureThumbnailAsync(card.FilePath, card.DateModified);

    /// <summary>
    /// Ensures a cached thumbnail exists for an arbitrary card PNG (scene or
    /// character), keyed by file path + last-modified time. Works on any PNG
    /// whose leading image is the preview to show.
    /// </summary>
    public async Task<string?> EnsureThumbnailAsync(string filePath, DateTime dateModified)
    {
        try
        {
            var cacheKey = ComputeCacheKey(filePath, dateModified);
            var cachePath = Path.Combine(_cacheFolder, $"{cacheKey}.jpg");

            if (File.Exists(cachePath))
                return cachePath;

            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);

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

            var cacheFile = await StorageFolder.GetFolderFromPathAsync(_cacheFolder);
            var outputFile = await cacheFile.CreateFileAsync(
                $"{cacheKey}.jpg",
                CreationCollisionOption.ReplaceExisting);

            using var outputStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outputStream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                ThumbnailWidth,
                scaledHeight,
                decoder.DpiX,
                decoder.DpiY,
                pixels.DetachPixelData());

            await encoder.FlushAsync();
            return cachePath;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task ClearCacheAsync()
    {
        await Task.Run(() =>
        {
            if (!Directory.Exists(_cacheFolder)) return;
            foreach (var file in Directory.EnumerateFiles(_cacheFolder, "*.jpg"))
            {
                try { File.Delete(file); } catch (Exception) { }
            }
        });
    }

    private static string ComputeCacheKey(string filePath, DateTime dateModified)
    {
        var input = $"{filePath}|{dateModified.Ticks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16];
    }
}

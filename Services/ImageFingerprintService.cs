using System.Runtime.InteropServices.WindowsRuntime;
using KoikatsuSceneGallery.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace KoikatsuSceneGallery.Services;

public readonly record struct ImageFingerprint(ulong PHash, float[] Histogram);

public static class ImageFingerprintService
{
    private const int HashSize = ImageFingerprintPixels.HashSize;

    public static async Task<ImageFingerprint?> ComputeAsync(string filePath, CancellationToken ct)
    {
        try
        {
            byte[] pngBytes;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                long pngSize = PngEmbeddedData.GetPngSize(fs);
                if (pngSize <= 0) return null;
                pngBytes = new byte[pngSize];
                fs.Position = 0;
                await fs.ReadExactlyAsync(pngBytes, ct).ConfigureAwait(false);
            }

            using var ms = new InMemoryRandomAccessStream();
            await ms.WriteAsync(pngBytes.AsBuffer()).AsTask(ct).ConfigureAwait(false);
            ms.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(ms).AsTask(ct).ConfigureAwait(false);
            var transform = new BitmapTransform
            {
                ScaledWidth = HashSize,
                ScaledHeight = HashSize,
                InterpolationMode = BitmapInterpolationMode.Linear,
            };

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);

            var pixels = pixelData.DetachPixelData();
            var (hash, histogram) = ImageFingerprintPixels.Compute(pixels);
            return new ImageFingerprint(hash, histogram);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }
}

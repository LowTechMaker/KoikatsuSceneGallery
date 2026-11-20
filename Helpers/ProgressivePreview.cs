using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using KoikatsuSceneGallery.Controls;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Shows a detail page's large image without a blank gap: the gallery's
/// thumbnail first, the full picture as soon as it has decoded.
/// </summary>
/// <remarks>
/// Assigning a new <see cref="BitmapImage"/> clears the old picture at once and
/// draws nothing until the new one decodes, so stepping through cards flashed
/// an empty frame each time. The thumbnail is already on disk and small, so it
/// fills that gap at the right size and aspect; the full image is decoded off
/// to the side with <see cref="BitmapSource.SetSourceAsync"/> and swapped in.
/// A newer card wins over a slower older decode.
/// </remarks>
internal sealed class ProgressivePreview(Image image)
{
    private const int MaxDecodeWidth = 1920;
    private int _generation;

    public void Show(CardBase card, int pixelWidth)
    {
        var generation = ++_generation;
        var decodeWidth = Math.Min(pixelWidth, MaxDecodeWidth);
        if (card.ThumbnailUri is not { } thumbnail)
        {
            image.Source = Direct(card, decodeWidth);
            return;
        }

        var standIn = new BitmapImage { DecodePixelWidth = UiMetrics.ThumbnailDecodeWidth, UriSource = thumbnail };
        image.Source = standIn;
        LoadFullAsync(card, decodeWidth, generation, standIn)
            .Observe(App.Services.GetRequiredService<IAppLogger>(), "Detail.LoadPreview");
    }

    private async Task LoadFullAsync(CardBase card, int decodeWidth, int generation, BitmapImage standIn)
    {
        var full = new BitmapImage { DecodePixelWidth = decodeWidth };
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(card.FilePath);
            using var stream = await file.OpenReadAsync();
            await full.SetSourceAsync(stream);
        }
        catch (Exception)
        {
            // The direct load reports its own failure the way it always has.
            if (Current(generation, standIn)) image.Source = Direct(card, decodeWidth);
            return;
        }

        if (Current(generation, standIn)) image.Source = full;
    }

    // Still this card, and nothing else has replaced the stand-in meanwhile:
    // the viewer's 100% button puts its own full-size source on the image, and
    // a decode finishing after that must not shrink it back.
    private bool Current(int generation, BitmapImage standIn) =>
        generation == _generation && ReferenceEquals(image.Source, standIn);

    private static BitmapImage Direct(CardBase card, int decodeWidth) =>
        new() { DecodePixelWidth = decodeWidth, UriSource = card.FileUri };
}

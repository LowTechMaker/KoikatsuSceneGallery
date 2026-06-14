using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Models;

public partial class MediaCard : ObservableObject
{
    public required string FilePath { get; init; }
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public long FileSize { get; init; }
    public DateTime DateModified { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Resolution => $"{Width}x{Height}";
    public bool IsVideo { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThumbnailUri))]
    [NotifyPropertyChangedFor(nameof(ThumbnailSource))]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    public partial string? ThumbnailPath { get; set; }

    public Uri FileUri => new(FilePath);
    public Uri? ThumbnailUri => ThumbnailPath != null ? new(ThumbnailPath) : null;

    private BitmapImage? _thumbnailSource;

    public BitmapImage? ThumbnailSource
    {
        get
        {
            if (ThumbnailPath is null) return null;
            return _thumbnailSource ??= new BitmapImage(new Uri(ThumbnailPath)) { DecodePixelWidth = 300 };
        }
    }

    partial void OnThumbnailPathChanged(string? value) => _thumbnailSource = null;

    public bool HasThumbnail => ThumbnailPath != null;
}

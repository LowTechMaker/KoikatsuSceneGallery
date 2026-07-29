using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Models;

public abstract partial class CardBase : ObservableObject
{
    public required string FilePath { get; init; }
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public long FileSize { get; init; }
    public DateTime DateModified { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Resolution => $"{Width}x{Height}";

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

    partial void OnThumbnailPathChanged(string? value)
    {
        _thumbnailSource = null;
        if (value is not null)
            ThumbnailGenerationFailed = false;
    }

    public bool HasThumbnail => ThumbnailPath != null;

    /// <summary>
    /// A terminal failure for this card snapshot. A new scan creates a new card
    /// (and therefore retries if the source file's timestamp or contents changed).
    /// </summary>
    private int _thumbnailGenerationFailed;
    public bool ThumbnailGenerationFailed
    {
        get => Volatile.Read(ref _thumbnailGenerationFailed) != 0;
        internal set => Volatile.Write(
            ref _thumbnailGenerationFailed,
            value ? 1 : 0);
    }

    public bool NeedsThumbnail => !HasThumbnail && !ThumbnailGenerationFailed;
}

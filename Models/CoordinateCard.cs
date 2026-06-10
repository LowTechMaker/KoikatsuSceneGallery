using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Models;

public partial class CoordinateCard : ObservableObject, IAuthorOwner
{
    public required string FilePath { get; init; }
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public long FileSize { get; init; }
    public DateTime DateModified { get; init; }
    public DateTime DateCreated { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Resolution => $"{Width}x{Height}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThumbnailUri))]
    [NotifyPropertyChangedFor(nameof(ThumbnailSource))]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    public partial string? ThumbnailPath { get; set; }

    [ObservableProperty]
    public partial bool MetadataLoaded { get; set; }

    [ObservableProperty]
    public partial string CoordinateName { get; set; } = string.Empty;

    /// <summary>Author resolved from the card's folder name by a plugin; null
    /// when no author plugin is installed or the folder carries no id.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthor))]
    public partial AuthorDisplay? Author { get; set; }

    public bool HasAuthor => Author != null;

    public Uri FileUri => new(FilePath);
    public Uri? ThumbnailUri => ThumbnailPath != null ? new(ThumbnailPath) : null;

    private BitmapImage? _thumbnailSource;
    /// <summary>
    /// Stable BitmapImage bound to the gallery Image.Source. Cached so x:Bind's
    /// phased re-evaluation gets the same instance; rebuilt only on path change.
    /// </summary>
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

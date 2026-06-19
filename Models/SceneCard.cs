using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Models;

public partial class SceneCard : ObservableObject, IAuthorOwner
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

    /// <summary>True once plugin metadata has been parsed (or read from cache).</summary>
    [ObservableProperty]
    public partial bool MetadataLoaded { get; set; }

    [ObservableProperty]
    public partial GameVersion Game { get; set; } = GameVersion.Unknown;

    [ObservableProperty]
    public partial bool IsR18Content { get; set; }

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
    /// phased re-evaluation gets the same instance (a fresh instance per read
    /// makes virtualized cells render inconsistently). Rebuilt only when the path
    /// changes; null when there is no thumbnail so Image.Source clears cleanly.
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

public record ResolutionOption(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";

    public static ResolutionOption? TryParse(string s)
    {
        var parts = s.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
            return new ResolutionOption(w, h);
        return null;
    }
}

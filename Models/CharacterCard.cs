using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Models;

/// <summary>
/// A Koikatsu character card (.png). Its leading PNG image is the card preview
/// shown in the gallery, so it reuses the same thumbnail pipeline as scenes.
/// Plugin/character metadata (name, game, classification) is added in a later
/// phase; this foundation models only the file-level information.
/// </summary>
public partial class CharacterCard : ObservableObject, IAuthorOwner
{
    public required string FilePath { get; init; }
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public long FileSize { get; init; }
    public DateTime DateModified { get; init; }
    public DateTime DateCreated { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Resolution => $"{Width}x{Height}";

    public DateTime FileTimestamp => CharacterCardFilenameParser.ParseTimestamp(FileName) ?? DateCreated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThumbnailUri))]
    [NotifyPropertyChangedFor(nameof(ThumbnailSource))]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    public partial string? ThumbnailPath { get; set; }

    /// <summary>True once embedded character metadata has been parsed (or read from cache).</summary>
    [ObservableProperty]
    public partial bool MetadataLoaded { get; set; }

    /// <summary>Full character name (last + first), used for display and search.</summary>
    [ObservableProperty]
    public partial string CharacterName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial GameVersion Game { get; set; } = GameVersion.Unknown;

    [ObservableProperty]
    public partial bool IsMadevil { get; set; }

    [ObservableProperty]
    public partial CardSource Source { get; set; } = CardSource.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersions))]
    public partial int VersionCount { get; set; } = 1;

    public bool HasVersions => VersionCount > 1;

    [ObservableProperty]
    public partial bool IsLatestVersion { get; set; } = true;

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

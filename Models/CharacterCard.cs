using CommunityToolkit.Mvvm.ComponentModel;

namespace KoikatsuSceneGallery.Models;

/// <summary>
/// A Koikatsu character card (.png). Its leading PNG image is the card preview
/// shown in the gallery, so it reuses the same thumbnail pipeline as scenes.
/// Plugin/character metadata (name, game, classification) is added in a later
/// phase; this foundation models only the file-level information.
/// </summary>
public partial class CharacterCard : ObservableObject
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

    public Uri FileUri => new(FilePath);
    public Uri? ThumbnailUri => ThumbnailPath != null ? new(ThumbnailPath) : null;
    public bool HasThumbnail => ThumbnailPath != null;
}

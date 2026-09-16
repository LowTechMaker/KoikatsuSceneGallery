using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Models;

/// <summary>
/// One file waiting to be assigned to a local source.
/// </summary>
/// <remarks>
/// Deliberately not an <see cref="ImportItem"/>: staging only needs to know
/// the file is a card and how to show it, and analysis builds its own
/// ImportItems anyway, so a staged one would just be discarded. Observable
/// only for <see cref="ThumbnailPath"/>, which arrives later.
/// </remarks>
public sealed partial class StagedCard : ObservableObject
{
    public required string FilePath { get; init; }

    public required CardType CardType { get; init; }

    public required GameVersion GameVersion { get; init; }

    public required DateTime DateModified { get; init; }

    public string FileName => Path.GetFileName(FilePath);

    [ObservableProperty]
    public partial string? ThumbnailPath { get; set; }
}

using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.ViewModels;

public partial class CoordinateDetailViewModel : ObservableObject
{
    private FilenameLinkInfo _linkInfo = FilenameLinkParser.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PixivArtworkId))]
    [NotifyPropertyChangedFor(nameof(HasPixivArtworkId))]
    [NotifyPropertyChangedFor(nameof(BepisDbId))]
    [NotifyPropertyChangedFor(nameof(BepisDbUrl))]
    [NotifyPropertyChangedFor(nameof(HasBepisDbUrl))]
    public partial CoordinateCard? Card { get; set; }

    partial void OnCardChanged(CoordinateCard? value) =>
        _linkInfo = FilenameLinkParser.Parse(value?.FilePath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParsing))]
    public partial bool MetadataLoaded { get; set; }

    public bool IsParsing => !MetadataLoaded;

    [ObservableProperty]
    public partial string CoordinateName { get; set; } = string.Empty;

    public string? PixivArtworkId => _linkInfo.PixivArtworkId;
    public string? PixivUrl => _linkInfo.PixivUrl;
    public bool HasPixivArtworkId => _linkInfo.PixivArtworkId != null;
    public string? BepisDbId => _linkInfo.BepisDbId;
    public string? BepisDbUrl => _linkInfo.BepisDbUrl;
    public bool HasBepisDbUrl => _linkInfo.BepisDbUrl != null;
}

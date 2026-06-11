using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.ViewModels;

public partial class CharacterDetailViewModel : ObservableObject
{
    private FilenameLinkInfo _linkInfo = FilenameLinkParser.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PixivArtworkId))]
    [NotifyPropertyChangedFor(nameof(HasPixivArtworkId))]
    [NotifyPropertyChangedFor(nameof(BepisDbId))]
    [NotifyPropertyChangedFor(nameof(BepisDbUrl))]
    [NotifyPropertyChangedFor(nameof(HasBepisDbUrl))]
    public partial CharacterCard? Card { get; set; }

    partial void OnCardChanged(CharacterCard? value) =>
        _linkInfo = FilenameLinkParser.Parse(value?.FilePath);

    /// <summary>True once the opened card's metadata has been parsed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParsing))]
    public partial bool MetadataLoaded { get; set; }

    public bool IsParsing => !MetadataLoaded;

    [ObservableProperty]
    public partial string FullName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Nickname { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SexDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GameDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MadevilDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<CharacterCard>? Versions { get; set; }

    [ObservableProperty]
    public partial bool HasMultipleVersions { get; set; }

    [ObservableProperty]
    public partial int VersionIndex { get; set; }

    [ObservableProperty]
    public partial int TotalVersions { get; set; }

    public string? PixivArtworkId => _linkInfo.PixivArtworkId;
    public string? PixivUrl => _linkInfo.PixivUrl;
    public bool HasPixivArtworkId => _linkInfo.PixivArtworkId != null;
    public string? BepisDbId => _linkInfo.BepisDbId;
    public string? BepisDbUrl => _linkInfo.BepisDbUrl;
    public bool HasBepisDbUrl => _linkInfo.BepisDbUrl != null;
}

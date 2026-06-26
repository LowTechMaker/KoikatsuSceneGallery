using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Models;

public partial class CharacterCard : CardBase, IAuthorOwner
{
    public DateTime DateCreated { get; init; }

    public DateTime FileTimestamp => CharacterCardFilenameParser.ParseTimestamp(FileName) ?? DateCreated;

    [ObservableProperty]
    public partial bool MetadataLoaded { get; set; }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthor))]
    public partial AuthorDisplay? Author { get; set; }

    public bool HasAuthor => Author != null;
}

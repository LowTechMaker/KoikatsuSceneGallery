using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Models;

public partial class CharacterCard : CardBase, IAuthorOwner
{
    public DateTime DateCreated { get; init; }

    public CharacterVariantKind VariantKind { get; init; }

    public string? FriendFolderPath { get; init; }

    public bool IsOldVersion => VariantKind == CharacterVariantKind.OldVersion;

    public bool IsAlternative => VariantKind == CharacterVariantKind.Alternative;

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

    /// <summary>
    /// True for the single card that stands in for its whole version group.
    /// Defaults to false so a card that has not been grouped yet — its
    /// metadata is still being parsed — cannot flash into the gallery.
    /// </summary>
    [ObservableProperty]
    public partial bool IsVersionGroupRepresentative { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthor))]
    public partial AuthorDisplay? Author { get; set; }

    public bool HasAuthor => Author != null;
}

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
    public partial CardMetadataSummary? MetadataSummary { get; set; }

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

    // ── What the user recorded about this card ────────────────────
    //
    // Persisted beside the card by CharacterAnnotationStore; everything above
    // is derived from the file itself.

    /// <summary>What this card is, relative to the character's other cards.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlternateVersion))]
    [NotifyPropertyChangedFor(nameof(HasVersionLabel))]
    [NotifyPropertyChangedFor(nameof(VersionLabelText))]
    public partial CharacterVersionKind VersionKind { get; set; }

    /// <summary>
    /// Whether a newer card has replaced this one. Separate from
    /// <see cref="VersionKind"/> so a replaced what-if version can say both.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersionLabel))]
    [NotifyPropertyChangedFor(nameof(VersionLabelText))]
    public partial bool IsSuperseded { get; set; }

    /// <summary>The user's own note, for example "short hair what-if".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersionNote))]
    public partial string? VersionNote { get; set; }

    /// <summary>
    /// The character this card is filed under, overriding its own name. Set
    /// when the user attaches a renamed card to the character it belongs to.
    /// </summary>
    [ObservableProperty]
    public partial string? CharacterGroupKey { get; set; }

    /// <summary>Derived, not stored: one source of truth for each fact.</summary>
    public bool IsAlternateVersion => VersionKind == CharacterVersionKind.Alternate;

    public bool HasVersionLabel => VersionKind != CharacterVersionKind.Current || IsSuperseded;

    public bool HasVersionNote => !string.IsNullOrWhiteSpace(VersionNote);

    /// <summary>
    /// How many of this character's cards are live what-if versions.
    /// </summary>
    /// <remarks>
    /// Set for every card of a character by the gallery's ranking pass, so the
    /// tile — which only ever shows the representative — can still say that the
    /// character has variants behind it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlternateVersions))]
    public partial int AlternateVersionCount { get; set; }

    public bool HasAlternateVersions => AlternateVersionCount > 0;

    public string VersionLabelText
    {
        get
        {
            var kind = VersionKind == CharacterVersionKind.Alternate
                ? UiText.Get("Character_VersionKind_Alternate")
                : UiText.Get("Character_VersionKind_Current");
            return IsSuperseded
                ? UiText.Format("Character_VersionSuperseded", kind)
                : kind;
        }
    }

    /// <summary>
    /// The version-index key this card is currently filed under.
    /// </summary>
    /// <remarks>
    /// Not observable, and set only by the gallery view model. Removal used to
    /// re-derive the key from the character name, which breaks as soon as the
    /// key can be overridden or the name can change between add and remove.
    /// </remarks>
    internal string? IndexedVersionKey { get; set; }
}

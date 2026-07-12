using System.Text.Json.Serialization;

namespace KoikatsuSceneGallery.Models;

/// <summary>
/// Character-card fields parsed from the embedded data: the character's name,
/// sex, game version (shared <see cref="GameVersion"/> with scenes) and whether
/// the card carries Madevil repack plugin data (same "madevil." GUID-prefix
/// signature used for scenes). Phase 2a surfaces these in the detail view only;
/// classification/filtering comes later.
/// </summary>
public sealed record CharacterMetadata(
    string? LastName,
    string? FirstName,
    string? Nickname,
    int Sex,
    GameVersion Game,
    bool IsMadevil)
{
    public const int SexMale = 0;
    public const int SexFemale = 1;

    /// <summary>Sentinel for a card that couldn't be parsed (cached so it isn't retried each scan).</summary>
    public static readonly CharacterMetadata Unknown = new(null, null, null, -1, GameVersion.Unknown, false);

    /// <summary>Last + first name joined for display (handles either being empty).</summary>
    [JsonIgnore]
    public string FullName =>
        string.Join(" ", new[] { LastName, FirstName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Derived unified source/version classification.</summary>
    [JsonIgnore]
    public CardSource Source => CardSourceClassifier.Classify(Game, IsMadevil);
}

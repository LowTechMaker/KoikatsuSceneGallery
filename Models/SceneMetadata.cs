namespace KoikatsuSceneGallery.Models;

/// <summary>
/// Which Koikatsu repack/environment a scene was built in, inferred from the
/// plugin GUIDs embedded in the card. Only "Madevil" has a reliable positive
/// signature (plugins prefixed "madevil."); everything else that parses is
/// labelled NonMadevil, and unparseable cards are Unknown.
/// </summary>
public enum SceneEnvironment
{
    Unknown,
    Madevil,
    NonMadevil
}

/// <summary>
/// Which Illusion game a scene was made in, detected from the embedded
/// character-card marker (【KoiKatuCharaSun】 = Sunshine, 【KoiKatuChara】 = base
/// Koikatsu). Unknown when no character marker is present.
/// </summary>
public enum GameVersion
{
    Unknown,
    Koikatsu,
    KoikatsuSunshine
}

/// <summary>
/// Classification tags derived from a scene's embedded data.
/// </summary>
public sealed record SceneMetadata(bool UsesTimeline, SceneEnvironment Environment, GameVersion Game)
{
    public static readonly SceneMetadata Unknown = new(false, SceneEnvironment.Unknown, GameVersion.Unknown);
}

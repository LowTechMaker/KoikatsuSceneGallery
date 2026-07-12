namespace KoikatsuSceneGallery.Models;

public enum GameVersion
{
    Unknown,
    Koikatsu,
    KoikatsuSunshine
}

public sealed record SceneMetadata(GameVersion Game)
{
    public static readonly SceneMetadata Unknown = new(GameVersion.Unknown);
}

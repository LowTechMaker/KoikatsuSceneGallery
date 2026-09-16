namespace KoikatsuSceneGallery.Models;

public enum ThumbnailSizePreference
{
    Small,
    Medium,
    Large
}

public static class ThumbnailSizePreferenceExtensions
{
    public static double ToPixels(this ThumbnailSizePreference preference) => preference switch
    {
        ThumbnailSizePreference.Small => 180,
        ThumbnailSizePreference.Large => 300,
        _ => 240
    };
}

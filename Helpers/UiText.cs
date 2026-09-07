using Microsoft.Windows.ApplicationModel.Resources;

namespace KoikatsuSceneGallery.Helpers;

internal static class UiText
{
    private static readonly ResourceLoader Resources = new();
    public static string Get(string key) => Resources.GetString(key.Replace('.', '/'));
    public static string Format(string key, params object[] args) => string.Format(Get(key), args);
}

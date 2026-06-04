using Windows.Storage;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Resolves the app's local data folder for both packaged (MSIX) and unpackaged
/// (plain .exe) launches. <see cref="ApplicationData.Current"/> requires package
/// identity and throws when the app runs unpackaged, so that case falls back to
/// %LOCALAPPDATA%\KoikatsuSceneGallery. Packaged launches keep using their
/// existing per-package LocalState folder unchanged.
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> LazyLocalFolder = new(Resolve);

    /// <summary>Local data folder, guaranteed to exist.</summary>
    public static string LocalFolder => LazyLocalFolder.Value;

    private static string Resolve()
    {
        string path;
        try
        {
            path = ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KoikatsuSceneGallery");
        }

        Directory.CreateDirectory(path);
        return path;
    }
}

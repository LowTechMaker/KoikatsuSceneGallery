using Microsoft.Windows.ApplicationModel.Resources;

namespace KoikatsuSceneGallery.Helpers;

internal static class UiText
{
    private static readonly ResourceLoader Resources = new();

    /// <summary>
    /// The localized string for <paramref name="key"/>, or the key itself when
    /// there is no such resource.
    /// </summary>
    /// <remarks>
    /// <see cref="ResourceLoader.GetString"/> throws when a resource is
    /// missing, and this is called from property getters that XAML evaluates
    /// during layout — where an exception takes the whole window down. A
    /// missing string is a mistake worth seeing, not worth crashing over, so it
    /// surfaces as the key: visible in the UI, obvious in a screenshot, and
    /// harmless. This also covers the case of a running build meeting resources
    /// that were replaced underneath it.
    /// </remarks>
    public static string Get(string key)
    {
        try
        {
            var value = Resources.GetString(key.Replace('.', '/'));
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception)
        {
            return key;
        }
    }

    public static string Format(string key, params object[] args)
    {
        var format = Get(key);
        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            // A placeholder count that does not match the arguments is the same
            // class of mistake, and just as unworthy of a crash.
            return format;
        }
    }
}

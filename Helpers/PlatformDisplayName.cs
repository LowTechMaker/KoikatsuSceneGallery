namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// What a platform is called on screen.
/// </summary>
/// <remarks>
/// The SDK gives a plugin a <c>ProviderId</c> and no display name, so the
/// app supplies one for the platforms it knows and shows the raw id for anything
/// third-party. Casing matters to a reader — "BepisDB", not "bepisdb" — and
/// only a resource can carry that per language.
/// </remarks>
internal static class PlatformDisplayName
{
    /// <summary>The label for a platform, falling back to its raw id.</summary>
    public static string For(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return providerId;
        var key = $"Platform_{providerId}";
        var text = UiText.Get(key);
        // UiText returns the key itself when there is no such resource.
        return string.Equals(text, key, StringComparison.Ordinal) ? providerId : text;
    }
}

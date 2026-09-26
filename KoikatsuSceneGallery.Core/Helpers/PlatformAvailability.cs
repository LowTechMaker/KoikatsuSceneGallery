using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Which platforms the app may currently present, and what follows from that.
/// </summary>
/// <remarks>
/// A platform's data outlives its plugin: sidecars, folder names and artwork
/// ids all stay on disk after the plugin is removed, so features built on that
/// data keep working and keep offering the platform. The rule here is that they
/// must not — a platform is only offered while its plugin is installed.
///
/// The built-in local provider is always registered and is never a platform in
/// this sense; it has its own page.
/// </remarks>
public static class PlatformAvailability
{
    /// <summary>The installed platforms, local excluded, de-duplicated.</summary>
    public static IReadOnlyList<string> Online(IEnumerable<string> installedProviderIds)
        => [.. installedProviderIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && !LocalSourceIdentity.IsLocal(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Whether any platform feature should appear at all. With nothing
    /// installed there is no "online" to contrast with local, and no platform
    /// whose tags could be offered.
    /// </summary>
    public static bool HasAny(IEnumerable<string> installedProviderIds)
        => Online(installedProviderIds).Count > 0;

    /// <summary>
    /// Drops the groups whose platform is not installed, keeping the order of
    /// the rest.
    /// </summary>
    public static IReadOnlyList<TagCloudProviderGroup> RestrictToInstalled(
        IReadOnlyList<TagCloudProviderGroup> groups, IEnumerable<string> installedProviderIds)
    {
        var installed = Online(installedProviderIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. groups.Where(group => installed.Contains(group.ProviderId))];
    }
}

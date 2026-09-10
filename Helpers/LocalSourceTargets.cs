using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Works out which local source folders an import will populate, and the name
/// to record in each, so the library carries its own names rather than
/// depending on this machine's settings.
/// </summary>
/// <remarks>
/// The folder comes from the item's destination, not from
/// <c>ImportItem.AuthorDirectoryPath</c>: destination resolution only fills
/// that in for items that have an artwork identity, and a local card never
/// has one. Reading it here is what made an earlier version of this silently
/// record nothing at all.
/// </remarks>
internal static class LocalSourceTargets
{
    public static (string Directory, string Id, string DisplayName)[] Collect(
        IEnumerable<ImportItem> items)
        => [.. items
            .Where(item => LocalSourceIdentity.IsLocal(item.AuthorProviderId)
                           && !string.IsNullOrEmpty(item.AuthorId))
            .Select(item => (
                Directory: Path.GetDirectoryName(item.DestinationPath),
                Id: item.AuthorId!,
                DisplayName: string.IsNullOrWhiteSpace(item.AuthorName)
                    ? item.AuthorId!
                    : item.AuthorName))
            .Where(target => !string.IsNullOrEmpty(target.Directory))
            .Select(target => (target.Directory!, target.Id, target.DisplayName))
            .DistinctBy(target => target.Item1, StringComparer.OrdinalIgnoreCase)];
}

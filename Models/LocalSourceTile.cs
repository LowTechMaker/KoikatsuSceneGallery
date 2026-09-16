using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Models;

/// <summary>
/// One local source as the collection page shows it: the registry entry that
/// proves it exists, plus the author summary that gives it a face and card
/// counts.
/// </summary>
public sealed record LocalSourceTile(LocalSourceEntry Entry, AuthorSummary Summary);

/// <summary>
/// The trailing "add a source" cell. A singleton so the grid's item list can
/// hold it alongside real tiles and template selection can branch on type.
/// </summary>
public sealed class AddLocalSourceTile
{
    public static AddLocalSourceTile Instance { get; } = new();

    private AddLocalSourceTile()
    {
    }
}

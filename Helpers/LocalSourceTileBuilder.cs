using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Joins the local source registry with the author service's card counts to
/// produce the collection page's grid.
/// </summary>
/// <remarks>
/// The registry is authoritative for which sources exist, not the author
/// service. Author displays are created by resolving cards to folders, and
/// displays that lose every card are pruned, so a source the user just
/// created — or one whose cards have all been removed — is absent from
/// <see cref="AuthorInfoService.GetSummaries"/> entirely. It still has to
/// appear here, or it could never be a drop target.
/// </remarks>
internal static class LocalSourceTileBuilder
{
    public static List<LocalSourceTile> Build(
        IReadOnlyList<LocalSourceEntry> sources,
        IReadOnlyList<AuthorSummary> summaries)
    {
        var byId = new Dictionary<string, AuthorSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var summary in summaries)
        {
            if (LocalSourceIdentity.IsLocal(summary.Display.Key.ProviderId))
                byId[summary.Display.Key.Id] = summary;
        }

        var tiles = new List<LocalSourceTile>(sources.Count);
        foreach (var source in sources)
        {
            tiles.Add(new LocalSourceTile(
                source,
                byId.TryGetValue(source.Id, out var summary)
                    ? summary
                    : Synthesize(source)));
        }

        return [.. tiles.OrderBy(tile => tile.Summary.Display.Name, StringComparer.CurrentCulture)];
    }

    /// <summary>
    /// Builds a stand-in summary for a source with no cards.
    /// </summary>
    /// <remarks>
    /// Only for that case. A source that has cards keeps the author service's
    /// real <see cref="AuthorDisplay"/>, because that instance is the one the
    /// gallery cards carry and the detail page matches against.
    /// </remarks>
    private static AuthorSummary Synthesize(LocalSourceEntry source)
        => new(
            new AuthorDisplay(
                new AuthorKey(LocalSourceIdentity.ProviderId, source.Id),
                source.DisplayName,
                "")
            {
                AvatarPath = source.AvatarPath,
            },
            SceneCount: 0,
            CharacterCount: 0,
            CoordinateCount: 0,
            LastUpdated: DateTime.MinValue);
}

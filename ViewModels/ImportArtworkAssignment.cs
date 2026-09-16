using KoikatsuSceneGallery.Models;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

/// <summary>
/// Call on the UI thread; continuations retain its context for item notifications.
/// Callers own undo and grouping. Query cancellation/errors propagate without marking items ready.
/// </summary>
internal sealed class ImportArtworkAssignment(
    Func<ArtworkId, CancellationToken, Task<ArtworkInfo?>> fetch)
{
    public async Task ApplyAsync(IReadOnlyList<ImportItem> items, ArtworkId artworkId,
        Action<ImportItem> addAnalyzing, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            item.ArtworkId = artworkId;
            item.FetchedArtworkInfo = null;
            item.AuthorName = null;
            item.AuthorId = null;
            item.AuthorProviderId = null;
            item.Title = null;
            item.Tags = null;
            item.ErrorMessage = null;
            item.Status = ImportItemStatus.Analyzing;
            addAnalyzing(item);
        }

        var info = await fetch(artworkId, cancellationToken);
        foreach (var item in items)
        {
            item.FetchedArtworkInfo = info;
            if (info is not null)
            {
                item.AuthorName = info.AuthorName;
                item.AuthorId = info.AuthorId;
                item.AuthorProviderId = artworkId.ProviderId;
                item.Title = info.Title;
                item.Rating = info.Rating;
                item.Tags = info.Tags;
            }
            item.Status = ImportItemStatus.ReadyToImport;
        }
    }
}

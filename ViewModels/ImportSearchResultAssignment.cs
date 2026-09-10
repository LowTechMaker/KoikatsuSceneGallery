using KoikatsuSceneGallery.Models;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

internal enum ImportSearchResultAssignmentMode { Unknown, FetchFailed, FlatReview }

/// <summary>Applies search fields only; callers own undo, containers and destination resolution.</summary>
internal static class ImportSearchResultAssignment
{
    public static void Apply(ImportItem item, ReverseImageSearchResult result,
        ContentRating rating, ImportSearchResultAssignmentMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == ImportSearchResultAssignmentMode.FlatReview)
        {
            item.ArtworkId = result.ArtworkId;
            item.ManualArtworkId = result.ArtworkId?.Id;
        }
        else if (result.ArtworkId is not null)
        {
            item.ArtworkId = result.ArtworkId;
            if (mode == ImportSearchResultAssignmentMode.Unknown)
                item.ManualArtworkId = result.ArtworkId.Id;
        }
        item.AuthorName = result.AuthorName;
        item.AuthorId = result.AuthorId;
        item.AuthorProviderId = result.ArtworkId?.ProviderId;
        if (mode == ImportSearchResultAssignmentMode.FlatReview)
            item.ManualAuthorId = result.AuthorId;
        item.Title = result.Title;
        item.Rating = rating;
        item.Tags = [];
        if (mode != ImportSearchResultAssignmentMode.FlatReview)
            item.ManualAuthorId = result.AuthorId;
        item.ErrorMessage = null;
        item.Status = ImportItemStatus.ReadyToImport;
    }
}

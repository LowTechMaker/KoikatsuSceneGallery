using KoikatsuSceneGallery.Models;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

internal sealed record ImportReviewEditRequest(
    bool ApplyArtwork, bool ApplyAuthor, bool ApplyRating,
    string? ArtworkInput, string? ArtworkProviderId, string? AuthorId, string? AuthorProviderId,
    int RatingIndex);

internal enum ImportReviewEditStatus { Ready, IdentityPairRequired, NoChanges, InputUnresolved, FetchUnchanged }

internal sealed record ImportReviewEditResult(ImportReviewEditStatus Status, PreparedImportReviewEdit? Edit = null);

internal sealed record PreparedImportReviewEdit(
    ArtworkId? ArtworkId, ArtworkInfo? ArtworkInfo,
    string? AuthorId, string? AuthorProviderId, string? AuthorName, ContentRating? Rating)
{
    /// <summary>Updates fields only. True asks the caller to reclassify before setting ReadyToImport.</summary>
    public bool Apply(ImportItem item)
    {
        if (ArtworkId is not null && ArtworkInfo is not null)
        {
            item.ArtworkId = ArtworkId;
            item.ManualArtworkId = ArtworkId.Id;
            item.FetchedArtworkInfo = ArtworkInfo;
            item.AuthorName = ArtworkInfo.AuthorName;
            item.AuthorId = ArtworkInfo.AuthorId;
            item.AuthorProviderId = ArtworkId.ProviderId;
            item.Title = ArtworkInfo.Title;
            item.Tags = ArtworkInfo.Tags;
            item.Rating = ArtworkInfo.Rating;
        }
        if (AuthorId is not null)
        {
            item.ManualAuthorId = AuthorId;
            item.AuthorName = AuthorName;
            item.AuthorId = AuthorId;
            item.AuthorProviderId = AuthorProviderId;
        }
        if (Rating is not null) item.Rating = Rating.Value;
        return (AuthorId is not null || ArtworkInfo is not null) && !string.IsNullOrWhiteSpace(item.AuthorId);
    }
}

/// <summary>Prepares immutable edits without modifying items. Cancellation/errors propagate to the UI owner.</summary>
internal sealed class ImportReviewEditService(
    Func<string, string?, ArtworkId?> resolveArtwork,
    Func<ArtworkId, CancellationToken, Task<ArtworkInfo?>> fetchArtwork,
    Func<string, string?, string> resolveAuthorName)
{
    public static ImportReviewEditStatus Validate(ImportReviewEditRequest request)
    {
        var hasAuthor = request.ApplyAuthor && !string.IsNullOrWhiteSpace(request.AuthorId);
        var hasProvider = request.ApplyAuthor && !string.IsNullOrWhiteSpace(request.AuthorProviderId);
        if (hasAuthor != hasProvider) return ImportReviewEditStatus.IdentityPairRequired;
        return hasAuthor || (request.ApplyArtwork && !string.IsNullOrWhiteSpace(request.ArtworkInput))
            || GetRating(request) is not null ? ImportReviewEditStatus.Ready : ImportReviewEditStatus.NoChanges;
    }

    public async Task<ImportReviewEditResult> PrepareAsync(ImportReviewEditRequest request, CancellationToken token)
    {
        var status = Validate(request);
        if (status != ImportReviewEditStatus.Ready) return new(status);
        ArtworkId? artworkId = null;
        ArtworkInfo? artworkInfo = null;
        if (request.ApplyArtwork && !string.IsNullOrWhiteSpace(request.ArtworkInput))
        {
            artworkId = resolveArtwork(request.ArtworkInput, request.ArtworkProviderId);
            if (artworkId is null) return new(ImportReviewEditStatus.InputUnresolved);
            artworkInfo = await fetchArtwork(artworkId, token);
            if (artworkInfo is null) return new(ImportReviewEditStatus.FetchUnchanged);
        }
        var hasAuthor = request.ApplyAuthor && !string.IsNullOrWhiteSpace(request.AuthorId);
        var name = hasAuthor ? resolveAuthorName(request.AuthorId!, request.AuthorProviderId) : null;
        return new(ImportReviewEditStatus.Ready, new(artworkId, artworkInfo,
            hasAuthor ? request.AuthorId!.Trim() : null,
            hasAuthor ? request.AuthorProviderId!.Trim() : null, name, GetRating(request)));
    }

    private static ContentRating? GetRating(ImportReviewEditRequest request) =>
        (request.ApplyRating ? request.RatingIndex : 0) switch
        {
            1 => ContentRating.AllAges, 2 => ContentRating.R18, 3 => ContentRating.R18G, _ => null
        };
}

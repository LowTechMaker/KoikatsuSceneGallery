using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.ViewModels;

internal enum ImportAuthorAssignmentMode
{
    Unknown,
    FetchFailedSingle,
    FetchFailedBatch
}

/// <summary>Updates author fields only. Callers own validation, undo capture and regrouping.</summary>
internal static class ImportAuthorAssignment
{
    public static void Apply(IReadOnlyList<ImportItem> items, string authorId,
        string authorName, string? providerId, ImportAuthorAssignmentMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        foreach (var item in items)
        {
            if (mode != ImportAuthorAssignmentMode.FetchFailedSingle)
                item.ManualAuthorId = authorId;
            item.AuthorName = authorName;
            item.AuthorId = authorId;
            item.AuthorProviderId = mode == ImportAuthorAssignmentMode.Unknown
                ? providerId : providerId ?? item.ArtworkId?.ProviderId;
        }
    }
}

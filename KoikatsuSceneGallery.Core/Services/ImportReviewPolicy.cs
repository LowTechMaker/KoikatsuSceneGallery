using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public static class ImportReviewPolicy
{
    public static Helpers.ImportReviewSection Section(ImportItemStatus status, bool hasAuthor, bool hasArtwork)
        => IsIdentified(status, hasAuthor) ? Helpers.ImportReviewSection.Identified
            : hasArtwork && status is not (ImportItemStatus.Pending or ImportItemStatus.Analyzing)
                ? Helpers.ImportReviewSection.Unavailable : Helpers.ImportReviewSection.Unidentified;

    // Identification is independent of whether the destination is configured.
    public static bool IsIdentified(ImportItemStatus status, bool hasAuthor)
        => status == ImportItemStatus.AlreadyInLibrary || (hasAuthor
            && status is not (ImportItemStatus.Pending or ImportItemStatus.Analyzing or ImportItemStatus.Failed));

    public static bool CanExecute(ImportItemStatus status, string? destination)
        => status == ImportItemStatus.ReadyToImport && !string.IsNullOrWhiteSpace(destination);

    public static string Category(ImportItemStatus status, string? destination, bool hasAuthor, bool hasArtwork) => status switch
    {
        ImportItemStatus.AlreadyInLibrary => "Existing",
        ImportItemStatus.Pending or ImportItemStatus.Analyzing => "Analyzing",
        ImportItemStatus.Failed => "Failed",
        ImportItemStatus.Completed => "Completed",
        _ when hasArtwork && !hasAuthor => "Failed",
        _ when !hasAuthor || !CanExecute(status, destination) => "NeedsInfo",
        _ => "Ready"
    };
}

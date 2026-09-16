using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportSearchResultAssignmentTests
{
    [Theory]
    [InlineData(0, true, "new", "new")]
    [InlineData(0, false, "old", "manual-old")]
    [InlineData(1, true, "new", "manual-old")]
    [InlineData(1, false, "old", "manual-old")]
    [InlineData(2, true, "new", "new")]
    [InlineData(2, false, null, null)]
    public void PreservesEachEntrysArtworkPolicyAndCanUndo(int mode, bool hasArtwork,
        string? expectedArtwork, string? expectedManual)
    {
        var cached = new ArtworkInfo(new("cached", "cached"), "cached", "cached",
            "cached", null, ContentRating.AllAges, [], DateTimeOffset.UtcNow, false);
        var item = new ImportItem
        {
            SourceFilePath = "source.png", ArtworkId = new("old-provider", "old"),
            ManualArtworkId = "manual-old", FetchedArtworkInfo = cached,
            AuthorId = "old-author", AuthorName = "Old author", AuthorProviderId = "old-provider",
            ManualAuthorId = "old-manual-author", Tags = [new("old-tag", null)],
            ErrorMessage = "error", DestinationPath = "destination.png",
            Status = ImportItemStatus.Failed
        };
        var history = new ImportManualAssignmentHistory();
        history.Capture(ManualAssignmentSource.FlatReview, [item]);
        var before = history.TakeUndo()!.Items[0];
        var result = new ReverseImageSearchResult("search",
            hasArtwork ? new("new-provider", "new") : null, "New title", "New author",
            "new-author", 95, null, null);
        ImportSearchResultAssignment.Apply(item, result, ContentRating.R18G,
            (ImportSearchResultAssignmentMode)mode);
        Assert.Equal(expectedArtwork, item.ArtworkId?.Id);
        Assert.Equal(expectedManual, item.ManualArtworkId);
        Assert.Equal(hasArtwork ? "new-provider" : null, item.AuthorProviderId);
        Assert.Equal("New author", item.AuthorName);
        Assert.Equal("new-author", item.AuthorId);
        Assert.Equal("new-author", item.ManualAuthorId);
        Assert.Equal("New title", item.Title);
        Assert.Equal(ContentRating.R18G, item.Rating);
        Assert.Empty(item.Tags!);
        Assert.Null(item.ErrorMessage);
        Assert.Equal(ImportItemStatus.ReadyToImport, item.Status);
        Assert.Equal("destination.png", item.DestinationPath);
        Assert.Same(cached, item.FetchedArtworkInfo);
        ImportManualAssignmentHistory.Restore(before);
        history.Capture(ManualAssignmentSource.FlatReview, [item]);
        Assert.Equal(before, history.TakeUndo()!.Items[0]);
    }
}

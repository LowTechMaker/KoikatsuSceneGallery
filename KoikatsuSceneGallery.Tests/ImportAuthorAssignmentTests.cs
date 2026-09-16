using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportAuthorAssignmentTests
{
    private static ImportItem Item(string? provider = "artwork") => new()
    {
        SourceFilePath = "source.png",
        ArtworkId = provider is null ? null : new(provider, "post"),
        ManualAuthorId = "old-manual",
        AuthorId = "old",
        AuthorName = "Old author",
        AuthorProviderId = "old-provider",
        Rating = ContentRating.R18,
        Title = "title",
        DestinationPath = "destination.png",
        Status = ImportItemStatus.ReadyToImport,
        ErrorMessage = "error",
        ManualArtworkId = "manual-post"
    };

    [Theory]
    [InlineData(0, "new-id")]
    [InlineData(1, "old-manual")]
    [InlineData(2, "new-id")]
    public void ModesPreserveManualIdPolicyAndNonAuthorFields(int mode, string manual)
    {
        var item = Item();
        var before = new ImportManualAssignmentHistory();
        before.Capture(ManualAssignmentSource.FlatReview, [item]);
        var snapshot = Assert.Single(before.TakeUndo()!.Items);
        ImportAuthorAssignment.Apply([item], "new-id", "New author", "explicit", (ImportAuthorAssignmentMode)mode);
        Assert.Equal(manual, item.ManualAuthorId);
        Assert.Equal("new-id", item.AuthorId);
        Assert.Equal("New author", item.AuthorName);
        Assert.Equal("explicit", item.AuthorProviderId);
        var after = new ImportManualAssignmentHistory();
        after.Capture(ManualAssignmentSource.FlatReview, [item]);
        Assert.Equal(snapshot with
        {
            AuthorId = "new-id", AuthorName = "New author",
            AuthorProviderId = "explicit", ManualAuthorId = manual
        }, Assert.Single(after.TakeUndo()!.Items));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NullProviderFallbackIsPerItem(int mode)
    {
        var items = new[] { Item("first"), Item("second"), Item(null) };
        ImportAuthorAssignment.Apply(items, "id", "name", null, (ImportAuthorAssignmentMode)mode);
        Assert.Equal(mode == 0 ? null : "first", items[0].AuthorProviderId);
        Assert.Equal(mode == 0 ? null : "second", items[1].AuthorProviderId);
        Assert.Null(items[2].AuthorProviderId);
    }

    [Fact]
    public void EmptyProviderIsNotTreatedAsNull()
    {
        var item = Item();
        ImportAuthorAssignment.Apply([item], "id", "name", "", ImportAuthorAssignmentMode.FetchFailedBatch);
        Assert.Equal("", item.AuthorProviderId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AssignmentCanRestoreAllCapturedFields(int mode)
    {
        var item = Item();
        var history = new ImportManualAssignmentHistory();
        history.Capture(ManualAssignmentSource.FlatReview, [item]);
        var undo = history.TakeUndo()!;
        ImportAuthorAssignment.Apply([item], "id", "name", null, (ImportAuthorAssignmentMode)mode);
        ImportManualAssignmentHistory.Restore(Assert.Single(undo.Items));
        history.Capture(ManualAssignmentSource.FlatReview, [item]);
        Assert.Equal(undo.Items[0], history.TakeUndo()!.Items[0]);
    }

    [Theory]
    [InlineData(0, "old")]
    [InlineData(1, "old")]
    [InlineData(2, "intermediate")]
    public void UndoUsesBaselineForLegacySourcesAndCurrentStateForFlatReview(int source, string expected)
    {
        var item = Item();
        var history = new ImportManualAssignmentHistory();
        history.RememberBaseline(item);
        item.AuthorId = "intermediate";
        history.RememberBaseline(item);
        history.Capture((ManualAssignmentSource)source, [item]);
        item.AuthorId = "after";
        var undo = history.TakeUndo()!;
        Assert.Equal((ManualAssignmentSource)source, undo.Source);
        ImportManualAssignmentHistory.Restore(Assert.Single(undo.Items));
        Assert.Equal(expected, item.AuthorId);
        Assert.Null(history.TakeUndo());
    }

    [Fact]
    public void MissingBaselineFallsBackToCurrentAndClearDropsBothHistoryAndBaseline()
    {
        var item = Item();
        var history = new ImportManualAssignmentHistory();
        history.Capture(ManualAssignmentSource.Unknown, [item]);
        Assert.Equal("old", history.TakeUndo()!.Items[0].AuthorId);
        history.RememberBaseline(item);
        history.Capture(ManualAssignmentSource.Unknown, [item]);
        history.Clear();
        Assert.Null(history.TakeUndo());
        item.AuthorId = "current";
        history.Capture(ManualAssignmentSource.FetchFailed, [item]);
        Assert.Equal("current", history.TakeUndo()!.Items[0].AuthorId);
    }
}

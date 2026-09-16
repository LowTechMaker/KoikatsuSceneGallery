using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportArtworkAssignmentTests
{
    private static ArtworkInfo Info(string id) => new(new("provider", id), "author", "42",
        "title-" + id, "description", ContentRating.R18G, [new("tag", null)], DateTimeOffset.UtcNow, false);
    private static ImportItem Item() => new()
    {
        SourceFilePath = "source.png", ArtworkId = new("old", "old"),
        FetchedArtworkInfo = Info("old"), AuthorName = "old", AuthorId = "old",
        AuthorProviderId = "old", Title = "old", Tags = [new("old", null)],
        ErrorMessage = "old", Rating = ContentRating.R18, ManualArtworkId = "manual",
        DestinationPath = "target.png"
    };

    [Fact]
    public async Task PreparesEveryItemBeforeSingleQueryAndPublishesNewMetadata()
    {
        var items = new[] { Item(), Item() };
        var added = new List<ImportItem>();
        var info = Info("new");
        var calls = 0;
        using var cts = new CancellationTokenSource();
        var assignment = new ImportArtworkAssignment((id, token) =>
        {
            calls++;
            Assert.Equal(info.ArtworkId, id);
            Assert.Equal(cts.Token, token);
            Assert.Equal(items, added);
            return Task.FromResult<ArtworkInfo?>(info);
        });
        await assignment.ApplyAsync(items, info.ArtworkId, item =>
        {
            Assert.Equal(ImportItemStatus.Analyzing, item.Status);
            Assert.Null(item.FetchedArtworkInfo);
            Assert.Null(item.AuthorName);
            Assert.Null(item.AuthorId);
            Assert.Null(item.AuthorProviderId);
            Assert.Null(item.Title);
            Assert.Null(item.Tags);
            Assert.Null(item.ErrorMessage);
            Assert.Equal(info.ArtworkId, item.ArtworkId);
            added.Add(item);
        }, cts.Token);
        Assert.Equal(1, calls);
        Assert.All(items, item =>
        {
            Assert.Same(info, item.FetchedArtworkInfo);
            Assert.Equal(info.AuthorName, item.AuthorName);
            Assert.Equal(info.AuthorId, item.AuthorId);
            Assert.Equal(info.ArtworkId.ProviderId, item.AuthorProviderId);
            Assert.Equal(info.Title, item.Title);
            Assert.Equal(info.Rating, item.Rating);
            Assert.Same(info.Tags, item.Tags);
            Assert.Equal(ImportItemStatus.ReadyToImport, item.Status);
            Assert.Equal("manual", item.ManualArtworkId);
            Assert.Equal("target.png", item.DestinationPath);
            var document = PostMetadataMapper.ToDocument(item.FetchedArtworkInfo!, []);
            Assert.Equal("new", document.ArtworkId);
            Assert.Equal("title-new", document.Title);
        });
    }

    [Fact]
    public async Task NullResultClearsStaleCacheAndPreservesRating()
    {
        var item = Item();
        var assignment = new ImportArtworkAssignment((_, _) => Task.FromResult<ArtworkInfo?>(null));
        await assignment.ApplyAsync([item], new("provider", "missing"), _ => { }, default);
        Assert.Null(item.FetchedArtworkInfo);
        Assert.Null(item.AuthorId);
        Assert.Null(item.Title);
        Assert.Null(item.Tags);
        Assert.Equal(ContentRating.R18, item.Rating);
        Assert.Equal(ImportItemStatus.ReadyToImport, item.Status);
        // Same eligibility branch used when the VM builds an execution plan.
        var document = item.FetchedArtworkInfo is null ? null : PostMetadataMapper.ToDocument(item.FetchedArtworkInfo, []);
        Assert.Null(document);
    }

    [Fact]
    public async Task CancellationPropagatesWithoutMarkingReady()
    {
        var item = Item();
        using var cts = new CancellationTokenSource();
        var assignment = new ImportArtworkAssignment((_, token) =>
        {
            Assert.Equal(cts.Token, token);
            cts.Cancel();
            return Task.FromCanceled<ArtworkInfo?>(token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            assignment.ApplyAsync([item], new("provider", "new"), _ => { }, cts.Token));
        Assert.Equal(ImportItemStatus.Analyzing, item.Status);
        Assert.Null(item.FetchedArtworkInfo);
    }

    [Fact]
    public async Task UnexpectedFailurePropagatesWithoutMarkingReady()
    {
        var item = Item();
        var assignment = new ImportArtworkAssignment((_, _) => throw new IOException("expected"));
        await Assert.ThrowsAsync<IOException>(() =>
            assignment.ApplyAsync([item], new("provider", "new"), _ => { }, default));
        Assert.Equal(ImportItemStatus.Analyzing, item.Status);
        Assert.Null(item.FetchedArtworkInfo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UndoRestoresPriorCacheAfterSuccessOrNull(bool success)
    {
        var item = Item();
        var old = item.FetchedArtworkInfo;
        var history = new ImportManualAssignmentHistory();
        history.RememberBaseline(item);
        history.Capture(ManualAssignmentSource.Unknown, [item]);
        var assignment = new ImportArtworkAssignment((_, _) => Task.FromResult(success ? Info("new") : null));
        await assignment.ApplyAsync([item], new("provider", "new"), _ => { }, default);
        ImportManualAssignmentHistory.Restore(Assert.Single(history.TakeUndo()!.Items));
        Assert.Same(old, item.FetchedArtworkInfo);
        Assert.Equal(new ArtworkId("old", "old"), item.ArtworkId);
        Assert.Equal(ContentRating.R18, item.Rating);
    }
}

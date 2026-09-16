using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportReviewEditServiceTests
{
    private static readonly ArtworkInfo Info = new(new("post-provider", "post"), "Fetched author", "fetched",
        "Fetched title", null, ContentRating.R18, [new("tag", null)], DateTimeOffset.UtcNow, false);
    private static ImportReviewEditRequest Request(bool artwork = false, bool author = false,
        bool rating = false, int index = 3) => new(artwork, author, rating, "post", "post-provider",
            " manual ", " manual-provider ", index);
    private static ImportReviewEditService Service() => new((_, _) => Info.ArtworkId,
        (_, _) => Task.FromResult<ArtworkInfo?>(Info), (_, _) => "Manual author");
    private static ImportItem Item() => new()
    {
        SourceFilePath = "source.png", AuthorId = "old", AuthorName = "Old author",
        AuthorProviderId = "old-provider", Title = "Old title", Rating = ContentRating.AllAges,
        Status = ImportItemStatus.Failed, ErrorMessage = "old-error", DestinationPath = "target.png"
    };

    [Theory]
    [InlineData(null, "provider")]
    [InlineData("author", null)]
    [InlineData(" ", "provider")]
    public async Task IncompleteIdentityPreventsAllLookup(string? author, string? provider)
    {
        var service = new ImportReviewEditService((_, _) => throw new Exception("unexpected"),
            (_, _) => throw new Exception("unexpected"), (_, _) => throw new Exception("unexpected"));
        var result = await service.PrepareAsync(Request(true, true, true) with
            { AuthorId = author, AuthorProviderId = provider }, default);
        Assert.Equal(ImportReviewEditStatus.IdentityPairRequired, result.Status);
        Assert.Null(result.Edit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public async Task InvalidRatingAloneIsNoChange(int index)
    {
        var result = await Service().PrepareAsync(Request(rating: true, index: index), default);
        Assert.Equal(ImportReviewEditStatus.NoChanges, result.Status);
        Assert.Null(result.Edit);
    }

    [Theory]
    [InlineData(1, ContentRating.AllAges)]
    [InlineData(2, ContentRating.R18)]
    [InlineData(3, ContentRating.R18G)]
    public async Task RatingIndexMapsToExpectedValue(int index, ContentRating rating)
    {
        var result = await Service().PrepareAsync(Request(rating: true, index: index), default);
        Assert.Equal(rating, result.Edit!.Rating);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedArtworkPreparationPreservesItemsAndUndo(bool resolves)
    {
        var item = Item();
        var history = new ImportManualAssignmentHistory();
        history.Capture(ManualAssignmentSource.FlatReview, [item]);
        var fetches = 0;
        var service = new ImportReviewEditService((_, _) => resolves ? Info.ArtworkId : null,
            (_, _) => { fetches++; return Task.FromResult<ArtworkInfo?>(null); },
            (_, _) => throw new Exception("Must not resolve author after artwork failure"));
        var result = await service.PrepareAsync(Request(true, true, true), default);
        Assert.Equal(resolves ? ImportReviewEditStatus.FetchUnchanged : ImportReviewEditStatus.InputUnresolved, result.Status);
        Assert.Null(result.Edit);
        Assert.Equal(resolves ? 1 : 0, fetches);
        Assert.Equal("old", item.AuthorId);
        Assert.Equal("Old title", item.Title);
        Assert.Equal(ContentRating.AllAges, item.Rating);
        Assert.NotNull(history.TakeUndo());
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task AppliesPrecedenceAndPreservesUnspecifiedFields(bool artwork, bool author, bool rating)
    {
        var item = Item();
        var history = new ImportManualAssignmentHistory();
        history.Capture(ManualAssignmentSource.FlatReview, [item]);
        var before = history.TakeUndo()!.Items[0];
        var result = await Service().PrepareAsync(Request(artwork, author, rating), default);
        Assert.Equal(ImportReviewEditStatus.Ready, result.Status);
        Assert.Equal(artwork || author, result.Edit!.Apply(item));
        Assert.Equal(author ? "manual" : artwork ? "fetched" : "old", item.AuthorId);
        Assert.Equal(author ? "Manual author" : artwork ? "Fetched author" : "Old author", item.AuthorName);
        Assert.Equal(author ? "manual-provider" : artwork ? "post-provider" : "old-provider", item.AuthorProviderId);
        Assert.Equal(rating ? ContentRating.R18G : artwork ? ContentRating.R18 : ContentRating.AllAges, item.Rating);
        Assert.Equal(artwork ? "Fetched title" : "Old title", item.Title);
        Assert.Same(artwork ? Info : null, item.FetchedArtworkInfo);
        Assert.Equal("target.png", item.DestinationPath);
        Assert.Equal("old-error", item.ErrorMessage);
        Assert.Equal(ImportItemStatus.Failed, item.Status);
        ImportManualAssignmentHistory.Restore(before);
        history.Capture(ManualAssignmentSource.FlatReview, [item]);
        Assert.Equal(before, history.TakeUndo()!.Items[0]);
    }

    [Fact]
    public async Task RequestSnapshotAndOneFetchServeMultipleItems()
    {
        var completion = new TaskCompletionSource<ArtworkInfo?>();
        var calls = 0;
        using var cts = new CancellationTokenSource();
        var service = new ImportReviewEditService((input, _) => { Assert.Equal("post", input); return Info.ArtworkId; },
            (_, token) => { calls++; Assert.Equal(cts.Token, token); return completion.Task; },
            (id, _) => { Assert.Equal(" manual ", id); return "Snapshot author"; });
        var request = Request(true, true, true);
        var pending = service.PrepareAsync(request, cts.Token);
        request = request with { AuthorId = "changed", RatingIndex = 1 };
        completion.SetResult(Info);
        var result = await pending;
        var a = Item();
        var b = Item();
        result.Edit!.Apply(a);
        result.Edit.Apply(b);
        Assert.Equal(1, calls);
        Assert.Equal("manual", a.AuthorId);
        Assert.Equal("manual", b.AuthorId);
        Assert.Equal(ContentRating.R18G, a.Rating);
        Assert.Equal("changed", request.AuthorId);
    }

    [Fact]
    public async Task CancellationAndExceptionsPropagate()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceled = new ImportReviewEditService((_, _) => Info.ArtworkId,
            (_, token) => Task.FromCanceled<ArtworkInfo?>(token), (_, _) => "author");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.PrepareAsync(Request(true), cts.Token));
        var failed = new ImportReviewEditService((_, _) => Info.ArtworkId,
            (_, _) => throw new IOException("expected"), (_, _) => "author");
        await Assert.ThrowsAsync<IOException>(() => failed.PrepareAsync(Request(true), default));
    }

    [Fact]
    public async Task DisabledInputsDoNotApplyAndAuthorRatingKeepExistingMetadata()
    {
        var item = Item();
        item.FetchedArtworkInfo = Info;
        var result = await Service().PrepareAsync(Request(author: true, rating: true), default);
        result.Edit!.Apply(item);
        Assert.Same(Info, item.FetchedArtworkInfo);
        Assert.Null(item.ArtworkId);
        Assert.Equal(ImportReviewEditStatus.NoChanges,
            (await Service().PrepareAsync(Request(), default)).Status);
    }
}

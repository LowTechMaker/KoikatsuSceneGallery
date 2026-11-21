using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.IntegrationTests;

/// <summary>Walking an author's posts newest first and comparing each post's files with the dropped cards.</summary>
public sealed class AuthorPostCheckerTests
{
    private static readonly AuthorKey Author = new("pixiv", "42");

    private static ArtworkId Post(string id) => new("pixiv", id);

    private static ArtworkInfo Info(string id, string title) => new(
        Post(id), "Artist", "42", title, Description: id, ContentRating.AllAges, [], DateTimeOffset.UnixEpoch, false);

    private static PostLinkInfo Link(params (string Name, long Size)[] files) => new(
        "https://mega.nz/folder/x#y", "MEGA", true, files.Select(f => new PostLinkFile(f.Name, f.Size)).ToList());

    // Posts by id; the description handed to the inspector is the id, which keys the links.
    private sealed class World
    {
        public List<string> PostOrder { get; } = [];
        public Dictionary<string, ArtworkInfo?> Infos { get; } = [];
        public Dictionary<string, PostLinkInfo[]> Links { get; } = [];
        public HashSet<string> Throws { get; } = [];
        public List<string> Fetched { get; } = [];

        public void Add(string id, string title, params PostLinkInfo[] links)
        {
            PostOrder.Add(id);
            Infos[id] = Info(id, title);
            Links[id] = links;
        }

        public AuthorPostChecker Checker(bool listFails = false, int grace = 0) => new(
            (_, _) => Task.FromResult<IReadOnlyList<ArtworkId>?>(listFails ? null : PostOrder.Select(Post).ToList()),
            (id, _) =>
            {
                Fetched.Add(id.Id);
                if (Throws.Contains(id.Id)) throw new HttpRequestException("403");
                return Task.FromResult(Infos[id.Id]);
            },
            (description, _) => Task.FromResult<IReadOnlyList<PostLinkInfo>>(
                description is not null && Links.TryGetValue(description, out var links) ? links : []),
            grace);
    }

    private sealed class Reports : IProgress<AuthorCheckProgress>
    {
        public List<AuthorCheckProgress> Items { get; } = [];
        public void Report(AuthorCheckProgress value) => Items.Add(value);
    }

    [Fact]
    public async Task OnlyPostsListingADroppedCardAreReportedAndNewestStaysFirst()
    {
        var world = new World();
        world.Add("300", "Newest", Link(("a.png", 1), ("b.png", 2), ("c.png", 3)));
        world.Add("200", "Unrelated", Link(("zzz.png", 9)));
        world.Add("100", "Oldest", Link(("d.png", 4)));

        var result = await world.Checker().CheckAsync(Author, ["a.png", "d.png", "x.png"], null, null, CancellationToken.None);

        Assert.Equal(AuthorCheckStatus.Completed, result.Status);
        Assert.Equal(["300", "100"], result.Entries.Select(e => e.Post.Id));
        Assert.Equal(["b.png", "c.png"], result.Entries[0].Match.Missing);
        Assert.Empty(result.Entries[1].Match.Missing);
        Assert.Equal((3, 3), (result.Total, result.Checked));
        Assert.Equal(["x.png"], result.UnclaimedCards);
    }

    [Fact]
    public async Task AFileTheLibraryHoldsIsInLibraryNotMissing()
    {
        var world = new World();
        world.Add("300", "Post", Link(("a.png", 10), ("b.png", 20), ("c.png", 30)));

        var result = await world.Checker().CheckAsync(
            Author, ["a.png"], (name, size) => name == "b.png" && size == 20, null, CancellationToken.None);

        var match = Assert.Single(result.Entries).Match;
        Assert.Equal(["b.png"], match.InLibrary);
        Assert.Equal(["c.png"], match.Missing);
    }

    [Fact]
    public async Task APostThatCannotBeFetchedIsCountedAndTheRunGoesOn()
    {
        var world = new World();
        world.Add("300", "Broken", Link(("a.png", 1)));
        world.Add("200", "Gone", Link(("a.png", 1)));
        world.Add("100", "Fine", Link(("a.png", 1)));
        world.Throws.Add("300");
        world.Infos["200"] = null;

        var result = await world.Checker().CheckAsync(Author, ["a.png"], null, null, CancellationToken.None);

        Assert.Equal(2, result.Unreadable);
        Assert.Equal(1, result.Checked);
        Assert.Equal(["100"], result.Entries.Select(e => e.Post.Id));
    }

    [Fact]
    public async Task APostWithoutReadableLinksIsCountedNotReported()
    {
        var world = new World();
        world.Add("300", "Plain text only");

        var result = await world.Checker().CheckAsync(Author, ["a.png"], null, null, CancellationToken.None);

        Assert.Equal(1, result.WithoutLinks);
        Assert.Empty(result.Entries);
        Assert.Equal(["a.png"], result.UnclaimedCards);
    }

    [Fact]
    public async Task AnAuthorThatCannotBeListedFailsTheCheckWithoutFetchingAnything()
    {
        var world = new World();
        world.Add("300", "Post", Link(("a.png", 1)));

        var result = await world.Checker(listFails: true).CheckAsync(Author, ["a.png"], null, null, CancellationToken.None);

        Assert.Equal(AuthorCheckStatus.ListFailed, result.Status);
        Assert.Empty(world.Fetched);
    }

    [Fact]
    public async Task EveryPostReportsProgressAndOnlyRelevantOnesCarryAnEntry()
    {
        var world = new World();
        world.Add("300", "Match", Link(("a.png", 1)));
        world.Add("200", "Other", Link(("q.png", 1)));
        var reports = new Reports();

        // The second card belongs to no post, so the run cannot stop early.
        await world.Checker().CheckAsync(Author, ["a.png", "orphan.png"], null, reports, CancellationToken.None);

        Assert.Equal([(1, 2), (2, 2)], reports.Items.Select(r => (r.Done, r.Total)));
        Assert.Equal(["300"], reports.Items.Where(r => r.Entry is not null).Select(r => r.Entry!.Post.Id));
    }

    [Fact]
    public async Task TheRunStopsOnceEveryDroppedCardHasAPost()
    {
        var world = new World();
        world.Add("300", "Has both", Link(("a.png", 1), ("b.png", 1)));
        world.Add("200", "Older", Link(("a.png", 1)));
        world.Add("100", "Oldest", Link(("b.png", 1)));

        var result = await world.Checker().CheckAsync(Author, ["a.png", "b.png"], null, null, CancellationToken.None);

        Assert.True(result.StoppedEarly);
        Assert.Equal(["300"], world.Fetched);
        Assert.Equal(3, result.Total);
        Assert.Single(result.Entries);
    }

    [Fact]
    public async Task ACardNoPostListsKeepsTheRunGoingToTheOldestPost()
    {
        var world = new World();
        world.Add("300", "A", Link(("a.png", 1)));
        world.Add("200", "B", Link(("q.png", 1)));

        var result = await world.Checker().CheckAsync(Author, ["a.png", "orphan.png"], null, null, CancellationToken.None);

        Assert.False(result.StoppedEarly);
        Assert.Equal(["300", "200"], world.Fetched);
        Assert.Equal(["orphan.png"], result.UnclaimedCards);
    }

    [Fact]
    public async Task ACardBelongsToTheMostSpecificPostThatListsIt()
    {
        var world = new World();
        // The newest post links the author's whole archive; older ones are about single works.
        world.Add("300", "Everything", Link(("a.png", 1), ("b.png", 1), ("c.png", 1), ("d.png", 1), ("x.png", 1), ("y.png", 1)));
        world.Add("200", "Series", Link(("c.png", 1), ("d.png", 1)));
        world.Add("100", "Single", Link(("a.png", 1)));

        var result = await world.Checker(grace: 8).CheckAsync(
            Author, ["a.png", "b.png", "c.png", "d.png"], null, null, CancellationToken.None);

        var byPost = result.Entries.ToDictionary(e => e.Post.Id, e => e.ClaimedCards!.Order().ToArray());
        Assert.Equal(["b.png"], byPost["300"]);
        Assert.Equal(["c.png", "d.png"], byPost["200"]);
        Assert.Equal(["a.png"], byPost["100"]);
    }

    [Fact]
    public async Task APostWhoseCardsAllBelongElsewhereIsACollection()
    {
        var world = new World();
        world.Add("300", "Everything", Link(("a.png", 1), ("b.png", 1), ("x.png", 1), ("y.png", 1)));
        world.Add("200", "One", Link(("a.png", 1)));
        world.Add("100", "Two", Link(("b.png", 1)));

        var result = await world.Checker(grace: 8).CheckAsync(Author, ["a.png", "b.png"], null, null, CancellationToken.None);

        var everything = result.Entries.Single(e => e.Post.Id == "300");
        Assert.True(everything.IsCollection);
        Assert.Empty(everything.ClaimedCards!);
        Assert.False(result.Entries.Single(e => e.Post.Id == "200").IsCollection);
    }

    [Fact]
    public async Task OnATieTheNewerPostWins()
    {
        var world = new World();
        world.Add("300", "Newer", Link(("a.png", 1), ("b.png", 1)));
        world.Add("200", "Older", Link(("a.png", 1), ("b.png", 1)));

        var result = await world.Checker(grace: 8).CheckAsync(Author, ["a.png"], null, null, CancellationToken.None);

        Assert.Equal(["a.png"], result.Entries.Single(e => e.Post.Id == "300").ClaimedCards!);
        Assert.Empty(result.Entries.Single(e => e.Post.Id == "200").ClaimedCards!);
    }

    [Fact]
    public async Task TheRunLooksAtSomeMorePostsAfterEveryCardHasOneBecauseTheRightHomeMayBeOlder()
    {
        var world = new World();
        world.Add("400", "Archive", Link(("a.png", 1), ("b.png", 1), ("x.png", 1)));
        world.Add("300", "Unrelated 1", Link(("q.png", 1)));
        world.Add("200", "Unrelated 2", Link(("r.png", 1)));
        world.Add("100", "The real home", Link(("a.png", 1)));

        var stopsSoon = await world.Checker(grace: 1).CheckAsync(Author, ["a.png"], null, null, CancellationToken.None);
        Assert.True(stopsSoon.StoppedEarly);
        Assert.Equal(["400", "300"], world.Fetched);

        world.Fetched.Clear();
        var looksFurther = await world.Checker(grace: 8).CheckAsync(Author, ["a.png"], null, null, CancellationToken.None);
        Assert.False(looksFurther.StoppedEarly);
        Assert.Equal(["400", "300", "200", "100"], world.Fetched);
        Assert.Equal(["a.png"], looksFurther.Entries.Single(e => e.Post.Id == "100").ClaimedCards!);
    }

    [Fact]
    public async Task AnEntryReportedWhileTheRunGoesOnDoesNotKnowItsCardsYet()
    {
        var world = new World();
        world.Add("300", "Post", Link(("a.png", 1)));
        var reports = new Reports();

        await world.Checker().CheckAsync(Author, ["a.png"], null, reports, CancellationToken.None);

        Assert.Null(reports.Items.Single(r => r.Entry is not null).Entry!.ClaimedCards);
    }

    [Fact]
    public async Task CancellationStopsTheRunInsteadOfBeingCounted()
    {
        var world = new World();
        world.Add("300", "A", Link(("a.png", 1)));
        world.Add("200", "B", Link(("a.png", 1)));
        using var cts = new CancellationTokenSource();
        var reports = new Reports();
        var checker = new AuthorPostChecker(
            (_, _) => Task.FromResult<IReadOnlyList<ArtworkId>?>(world.PostOrder.Select(Post).ToList()),
            (id, _) => { cts.Cancel(); return Task.FromResult(world.Infos[id.Id]); },
            (_, _) => Task.FromResult<IReadOnlyList<PostLinkInfo>>([]));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => checker.CheckAsync(Author, ["a.png"], null, reports, cts.Token));
    }
}

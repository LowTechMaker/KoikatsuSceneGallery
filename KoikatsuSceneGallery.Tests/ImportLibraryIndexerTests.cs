using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportLibraryIndexerTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ImportIndex-" + Guid.NewGuid());
        public LibraryFileCache Cache { get; } = new();
        public List<(string Operation, Exception Error, string? Path)> Errors { get; } = [];
        public ImportLibraryIndexer Indexer { get; }
        public Fixture() => Indexer = new(Cache, (operation, error, path) => Errors.Add((operation, error, path)));
        public string PathFor(string relative) => Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        public string DirectoryFor(string relative) => Directory.CreateDirectory(PathFor(relative)).FullName;
        public string FileFor(string relative, params byte[] bytes)
        {
            var path = PathFor(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        public Task<ImportLibraryIndexResult> Build(ImportLibraryIndexRequest request, CancellationToken token = default)
            => Indexer.BuildAsync(request, token).WaitAsync(TimeSpan.FromSeconds(15));
        public void Dispose()
        {
            // Only this fixture's generated directory can enter the cleanup target.
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private static ImportLibraryIndexRequest Request(string[] roots, params ImportLibrarySource[] sources)
        => new(sources, roots, "Organized", ["Game"], []);

    /// <summary>A 1x1 PNG; anything appended after it is card data.</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");

    /// <summary>A different, larger PNG, standing in for a re-encoded preview.</summary>
    private static readonly byte[] OtherPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAMAAAADCAIAAADZSiLoAAAAEElEQVR4nGM4kWIEQQxYWAC99gxPyJ2wDQAAAABJRU5ErkJggg==");

    private static byte[] CardBytes(byte[] png, string payload)
        => [.. png, .. System.Text.Encoding.UTF8.GetBytes(payload)];

    // The reported case: Windows added "(1)" to a second copy, so the library
    // index — keyed by exact file name — never even compared the two, and the
    // copy imported as a separate card.
    [Fact]
    public async Task ACopyWhoseNameGainedASuffixIsRecognizedByItsCardData()
    {
        using var f = new Fixture();
        var source = f.FileFor(
            "inputs/Koikatu_F_20260201060122285_demolition gun(1).png",
            CardBytes(OtherPng, "one character"));
        f.FileFor(
            "library/Koikatu_F_20260201060122285_demolition gun.png",
            CardBytes(TinyPng, "one character"));

        var result = await f.Build(Request(
            [f.PathFor("library")],
            new ImportLibrarySource(source, "Koikatu_F_20260201060122285_demolition gun(1).png")));

        Assert.Equal(source, Assert.Single(result.IdenticalSourcePaths));
        Assert.Equal(1, result.ActualIdenticalDuplicates);
        Assert.Empty(f.Errors);
    }

    // The narrowed name only decides what is worth reading. A genuinely
    // different card that happens to look like a copy must still import.
    [Fact]
    public async Task ADifferentCardWhoseNameLooksLikeACopyIsNotFoldedAway()
    {
        using var f = new Fixture();
        var source = f.FileFor("inputs/card(1).png", CardBytes(TinyPng, "a different character"));
        f.FileFor("library/card.png", CardBytes(TinyPng, "one character"));

        var result = await f.Build(Request([f.PathFor("library")], new ImportLibrarySource(source, "card(1).png")));

        Assert.Empty(result.IdenticalSourcePaths);
        Assert.Equal(1, result.ActualContentMismatches);
        Assert.Empty(f.Errors);
    }

    // Files with no card data at all must not all look like each other.
    [Fact]
    public async Task PlainImagesThatAreNotCardsAreNeverFoldedTogether()
    {
        using var f = new Fixture();
        var source = f.FileFor("inputs/plain(1).png", TinyPng);
        f.FileFor("library/plain.png", TinyPng);

        var result = await f.Build(Request([f.PathFor("library")], new ImportLibrarySource(source, "plain(1).png")));

        Assert.Empty(result.IdenticalSourcePaths);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task MultipleRootsCompareContentsAndPreserveDiagnosticCounts()
    {
        using var f = new Fixture();
        var source = f.FileFor("inputs/card.png", 1, 2, 3);
        var different = f.FileFor("inputs/different.png", 4, 5, 6);
        f.FileFor("first/nested/card.png", 1, 2, 4);
        f.FileFor("first/different.png", 4, 5, 7);
        f.FileFor("second/card.png", 1, 2, 3);
        f.FileFor("second/ignored.jpg", 1, 2, 3);
        var ignored = f.FileFor("inputs/ignored.jpg", 1, 2, 3);
        var request = Request([f.PathFor("first"), f.PathFor("missing"), f.PathFor("second")],
            new(source, "CARD.PNG"), new(different, "different.png"), new(ignored, "ignored.jpg"),
            new(f.PathFor("absent.png"), "card.png"));
        var result = await f.Build(request);
        Assert.Equal(source, Assert.Single(result.IdenticalSourcePaths));
        Assert.Contains(source.ToUpperInvariant(), result.IdenticalSourcePaths, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(3, result.TotalCandidatesCompared);
        Assert.Equal(1, result.ActualIdenticalDuplicates);
        Assert.Equal(2, result.ActualContentMismatches);
        Assert.True(result.LibraryScanAndCandidateIndexElapsedMs >= 0);
        Assert.True(result.ByteComparisonElapsedMs >= 0);
        Assert.True(result.FolderIndexElapsedMs >= 0);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task AuthorScopesPreserveFirstEnumeratedFolderAndCaseInsensitiveIds()
    {
        using var f = new Fixture();
        var root = f.DirectoryFor("library");
        var seen = new List<string>();
        foreach (var provider in new[] { "rated", "unrated" })
        foreach (var game in new[] { "Game", "Other", "" })
        foreach (var rating in provider == "rated" ? new[] { "G", "R18" } : new[] { "" })
        {
            var target = ImportDestinationPolicy.BuildTargetBase(root, "Organized", provider, game, rating, null);
            Directory.CreateDirectory(Path.Combine(target, "First"));
            Directory.CreateDirectory(Path.Combine(target, "Second"));
            Directory.CreateDirectory(Path.Combine(target, "Unknown"));
        }
        string? Parse(string name)
        {
            if (name is not ("First" or "Second")) return null;
            seen.Add(name);
            return name == "First" ? "Author" : "AUTHOR";
        }
        var request = new ImportLibraryIndexRequest([], [root], " Organized ", ["Game", "Other", ""],
            [new("rated", ["G", "R18"], Parse), new("unrated", [""], Parse)]);
        var result = await f.Build(request);
        Assert.Equal(9, result.FolderIndex.Count);
        var position = 0;
        foreach (var scope in request.ProviderScopes)
        foreach (var game in request.GameVersionFolders)
        foreach (var rating in scope.RatingFolders)
        {
            var key = ImportLibraryIndexer.BuildScopeKey(root, scope.Folder, game, rating);
            var authors = result.FolderIndex[key.ToUpperInvariant()];
            Assert.Single(authors);
            // Observe the actual enumeration order instead of imposing a filesystem order.
            var expectedName = seen[position];
            position += 2;
            Assert.Equal(Path.Combine(ImportDestinationPolicy.BuildTargetBase(root, "Organized",
                scope.Folder, game, rating, null), expectedName), authors["author"]);
        }
        Assert.Equal(18, seen.Count);
    }

    [Fact]
    public async Task CacheIsReusedRegisteredAndInvalidatedWithoutCachingAuthorFolders()
    {
        using var f = new Fixture();
        var root = f.DirectoryFor("library");
        var source = f.FileFor("inputs/card.png", 1, 2);
        var request = Request([root], new ImportLibrarySource(source, "card.png")) with
        {
            ProviderScopes = [new("provider", ["G"], name => name)]
        };
        Assert.Empty((await f.Build(request)).IdenticalSourcePaths);
        var candidate = f.FileFor("library/card.png", 1, 2);
        f.DirectoryFor("library/Organized/provider/Game/G/Author");
        var cached = await f.Build(request);
        Assert.Empty(cached.IdenticalSourcePaths);
        Assert.Equal(0, cached.TotalCandidatesCompared);
        Assert.Equal(0, cached.LibraryScanAndCandidateIndexElapsedMs);
        Assert.Single(cached.FolderIndex.Values.Single());
        f.Cache.RegisterCommittedFiles([candidate]);
        Assert.Single((await f.Build(request)).IdenticalSourcePaths);

        var addedSource = f.FileFor("inputs/added.png", 3, 4);
        f.FileFor("library/added.png", 3, 4);
        var addedRequest = request with { Sources = [new(addedSource, "added.png")] };
        Assert.Empty((await f.Build(addedRequest)).IdenticalSourcePaths);
        f.Cache.Invalidate();
        Assert.Single((await f.Build(addedRequest)).IdenticalSourcePaths);
    }

    [Fact]
    public async Task ChangingRootsRebuildsTheFilenameCache()
    {
        using var f = new Fixture();
        var first = f.DirectoryFor("first");
        var source = f.FileFor("inputs/card.png", 1);
        f.FileFor("second/card.png", 1);
        Assert.Empty((await f.Build(Request([first], new ImportLibrarySource(source, "card.png")))).IdenticalSourcePaths);
        Assert.Single((await f.Build(Request([f.PathFor("second")], new ImportLibrarySource(source, "card.png")))).IdenticalSourcePaths);
    }

    [Fact]
    public async Task PreCanceledBuildDoesNotParseAndCanRetry()
    {
        using var f = new Fixture();
        var root = f.DirectoryFor("library");
        f.DirectoryFor("library/Organized/provider/Game/G/Author");
        var calls = 0;
        var request = Request([root]) with { ProviderScopes = [new("provider", ["G"], name => { calls++; return name; })] };
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Build(request, cancel.Token));
        Assert.Equal(0, calls);
        await f.Build(request);
        Assert.Equal(1, calls);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task CancellationFromAuthorParserPropagatesWithoutLogging()
    {
        using var f = new Fixture();
        var root = f.DirectoryFor("library");
        f.DirectoryFor("library/Organized/provider/Game/G/Author");
        using var cancel = new CancellationTokenSource();
        var request = Request([root]) with
        {
            ProviderScopes = [new("provider", ["G"], _ =>
            {
                cancel.Cancel();
                cancel.Token.ThrowIfCancellationRequested();
                return null;
            })]
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Build(request, cancel.Token));
        Assert.Empty(f.Errors);
        await f.Build(request with { ProviderScopes = [new("provider", ["G"], name => name)] });
    }

    [Fact]
    public async Task ParserErrorRetainsPartialScopeAndContinuesNextScope()
    {
        using var f = new Fixture();
        var root = f.DirectoryFor("library");
        f.DirectoryFor("library/Organized/provider/Game/G/One");
        f.DirectoryFor("library/Organized/provider/Game/G/Two");
        f.DirectoryFor("library/Organized/provider/Game/R18/Three");
        var calls = 0;
        var error = new IOException("parser failed");
        var request = Request([root]) with
        {
            ProviderScopes = [new("provider", ["G", "R18"], name => ++calls == 2 ? throw error : name)]
        };
        var result = await f.Build(request);
        Assert.All(result.FolderIndex.Values, authors => Assert.Single(authors));
        Assert.Equal(3, calls);
        var logged = Assert.Single(f.Errors);
        Assert.Equal("Import.BuildFolderIndex", logged.Operation);
        Assert.Same(error, logged.Error);
        Assert.Equal(f.PathFor("library/Organized/provider/Game/G"), logged.Path);
    }
}

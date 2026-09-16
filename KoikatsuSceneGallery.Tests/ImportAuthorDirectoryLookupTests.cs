using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportAuthorDirectoryLookupTests
{
    private static Dictionary<string, Dictionary<string, string>> Index() => new(StringComparer.OrdinalIgnoreCase);
    private static void Add(Dictionary<string, Dictionary<string, string>> index, string root,
        string provider, string game, string rating, string directory, string author = "author")
        => index.Add(ImportLibraryIndexer.BuildScopeKey(root, provider, game, rating),
            new(StringComparer.OrdinalIgnoreCase) { [author] = directory });

    [Fact]
    public void ExactMatchPreservesRootOrderAndIndexComparers()
    {
        var index = Index();
        Add(index, "first", "provider", "game", "rating", "first-directory");
        Add(index, "second", "provider", "game", "rating", "second-directory");
        Assert.Equal("first-directory", ImportAuthorDirectoryLookup.FindExact(
            ["first", "second"], "PROVIDER", "game", "rating", "AUTHOR", index));
        Assert.Null(ImportAuthorDirectoryLookup.FindExact(["first"], "provider", "game", "other", "author", index));
    }

    [Fact]
    public void EarlierAlternateGameStopsBeforeLaterExactProviderOrRoot()
    {
        var index = Index();
        Add(index, "first", "early", "other-game", "rating", "not-reused");
        Add(index, "first", "later", "game", "rating", "later-directory");
        Add(index, "second", "early", "game", "rating", "second-root");
        var match = ImportAuthorDirectoryLookup.FindFallback(["first", "second"],
            [("early", true), ("later", true)], ["game", "other-game"], "game", "rating", "author", index);
        Assert.Null(match.Directory);
        Assert.Equal("early", match.ProviderFolder);
    }

    [Theory]
    [InlineData(true, "rating")]
    [InlineData(false, "")]
    public void ExactFallbackUsesProviderRatingScopeAndWinsOverItsAlternative(bool usesRating, string indexedRating)
    {
        var index = Index();
        Add(index, "root", "provider", "game", indexedRating, "exact");
        Add(index, "root", "provider", "other-game", indexedRating, "alternative");
        var match = ImportAuthorDirectoryLookup.FindFallback(["root"], [("provider", usesRating)],
            ["other-game", "game"], "game", "rating", "author", index);
        Assert.Equal("exact", match.Directory);
        Assert.Null(match.ProviderFolder);
    }

    [Fact]
    public void NoMatchAndEmptyProviderFolderRemainDistinct()
    {
        var index = Index();
        Assert.Equal(((string?)null, (string?)null), ImportAuthorDirectoryLookup.FindFallback(
            ["root"], [("", false)], ["other-game"], "game", "rating", "author", index));
        Add(index, "root", "", "other-game", "", "existing");
        var match = ImportAuthorDirectoryLookup.FindFallback(["root"], [("", false)],
            ["other-game"], "game", "rating", "author", index);
        Assert.Null(match.Directory);
        Assert.Equal("", match.ProviderFolder);
    }

    [Fact]
    public void FirstMatchDoesNotEnumerateLaterScopes()
    {
        var index = Index();
        Add(index, "root", "provider", "game", "rating", "existing");
        static IEnumerable<(string, bool)> Scopes()
        {
            yield return ("provider", true);
            throw new InvalidOperationException("must stop at first match");
        }
        Assert.Equal("existing", ImportAuthorDirectoryLookup.FindFallback(["root"], Scopes(),
            ["game"], "game", "rating", "author", index).Directory);
    }
}

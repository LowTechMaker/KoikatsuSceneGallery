using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class PostLinkTests
{
    private const string FileLink = "https://mega.nz/file/1T5zQTgS#2NuM_PDUlaC4bjSsojZe2oamUH-w53lsfUz8V4zrsVQ";
    private const string FolderLink = "https://mega.nz/folder/YeASSYaY#uFlrenikd-TGVkdACPLAcw";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no links here, only text")]
    public void NoLinksYieldsEmpty(string? description) => Assert.Empty(PostLinkExtractor.Extract(description));

    [Fact]
    public void PlainUrlsAreFoundInOrderAndTrimmed()
        => Assert.Equal([FileLink, FolderLink],
            PostLinkExtractor.Extract($"Scene: {FileLink}, characters: {FolderLink}."));

    [Fact]
    public void PixivJumpLinksAreUnwrappedAndDeduplicatedAgainstTheirText()
    {
        var encoded = Uri.EscapeDataString(FolderLink);
        var html = $"""Download<br />\n<a href="/jump.php?{encoded}" target="_blank">{FolderLink}</a>""";

        Assert.Equal([FolderLink], PostLinkExtractor.Extract(html));
    }

    [Fact]
    public void AbsolutePixivJumpLinkYieldsTheRealAddress()
    {
        const string jump = "https://www.pixiv.net/jump.php?https%3A%2F%2Fmega.nz%2Ffolder%2FgDByTYYA%236DlfNsE5Cr-0RaRl2ximGQ";

        Assert.Contains("https://mega.nz/folder/gDByTYYA#6DlfNsE5Cr-0RaRl2ximGQ", PostLinkExtractor.Extract(jump));
        Assert.Contains("https://mega.nz/folder/gDByTYYA#6DlfNsE5Cr-0RaRl2ximGQ",
            PostLinkExtractor.Extract($"<a href=\"{jump}\" target=\"_blank\">mega.nz/folder/gDBy...</a>"));
    }

    [Fact]
    public void HtmlEntitiesAreDecodedBeforeSearching()
        => Assert.Equal(["https://example.com/a?x=1&y=2"],
            PostLinkExtractor.Extract("see https://example.com/a?x=1&amp;y=2<br />"));

    [Fact]
    public void JumpLinkWithMalformedEscapesIsSkippedNotThrown()
        => Assert.Empty(PostLinkExtractor.Extract("<a href=\"/jump.php?%E0%A4%A\">x</a>"));

    [Fact]
    public void MatcherSplitsCardsIntoMatchedMissingAndUnlisted()
    {
        var match = PostLinkMatcher.Match(
            ["a.png", "b.png", "c.png"],
            ["B.PNG", "x.png", "a.png"]);

        Assert.Equal(3, match.ListedCount);
        Assert.Equal(["B.PNG", "a.png"], match.Matched);
        Assert.Equal(["c.png"], match.Missing);
        Assert.Equal(["x.png"], match.Unlisted);
    }

    [Fact]
    public void MatcherCountsARepeatedListedNameOnce()
    {
        var match = PostLinkMatcher.Match(["a.png", "A.png", " ", ""], ["a.png"]);

        Assert.Equal(1, match.ListedCount);
        Assert.Empty(match.Missing);
    }

    [Theory]
    [InlineData("2023_0109_1345_51_630 (1).png")]
    [InlineData("2023_0109_1345_51_630(1).png")]
    [InlineData("2023_0109_1345_51_630 (12).PNG")]
    public void AWindowsCopySuffixOnTheDroppedCardStillMatches(string card)
    {
        var match = PostLinkMatcher.Match(["2023_0109_1345_51_630.png"], [card]);

        Assert.Equal([card], match.Matched);
        Assert.Empty(match.Missing);
        Assert.Empty(match.Unlisted);
    }

    [Fact]
    public void AWindowsCopySuffixOnTheListedNameIsIgnoredToo()
    {
        var match = PostLinkMatcher.Match(["card (1).png"], ["card.png"]);

        Assert.Equal(["card.png"], match.Matched);
        Assert.Empty(match.Missing);
    }

    [Fact]
    public void TheNumberedPartOfAScenNameIsNotACopySuffix()
    {
        var match = PostLinkMatcher.Match(["2023_0109_1345_51_630.png"], ["2023_0109_1345_51_631.png"]);

        Assert.Empty(match.Matched);
        Assert.Equal(["2023_0109_1345_51_630.png"], match.Missing);
    }

    [Fact]
    public void AListedFileTheLibraryHoldsIsReportedApartFromAMissingOne()
    {
        var match = PostLinkMatcher.MatchFiles(
            [new ListedFile("a.png", 10), new ListedFile("b.png", 20), new ListedFile("c.png", 30)],
            ["b.png"],
            (name, size) => name == "a.png" && size == 10);

        Assert.Equal(["a.png"], match.InLibrary);
        Assert.Equal(["c.png"], match.Missing);
        Assert.Equal(["b.png"], match.Matched);
    }

    [Fact]
    public void TheLibraryIsNotAskedAboutAFileTheUserAlreadyDropped()
    {
        var asked = new List<string>();

        PostLinkMatcher.MatchFiles([new ListedFile("a.png", 1), new ListedFile("b.png", 2)], ["a.png"],
            (name, _) => { asked.Add(name); return false; });

        Assert.Equal(["b.png"], asked);
    }

    [Fact]
    public void WithoutALibraryNothingIsInLibrary()
    {
        var match = PostLinkMatcher.MatchFiles([new ListedFile("a.png", 1)], [], libraryHas: null);

        Assert.Empty(match.InLibrary);
        Assert.Equal(["a.png"], match.Missing);
    }

    [Fact]
    public void MissingAndInLibraryFilesKeepTheirAddresses()
    {
        var match = PostLinkMatcher.MatchFiles(
            [new ListedFile("a.png", 1, "https://x/a"), new ListedFile("b.png", 2, "https://x/b"), new ListedFile("c.png", 3)],
            [],
            (name, _) => name == "b.png");

        Assert.Equal(["https://x/a", null], match.MissingFiles.Select(f => f.Url));
        Assert.Equal(["https://x/b"], match.InLibraryFiles.Select(f => f.Url));
        Assert.Equal(match.MissingFiles.Select(f => f.Name), match.Missing);
    }

    // The shape of a real series share: the first episode loose in the root, the others in folders.
    private static ListedFile InFolder(string name, string? folder)
        => new(name, 1, null, folder, folder is null ? null : "https://x/folder/" + folder);

    [Fact]
    public void AnEpisodeNothingWasTakenFromIsNotStartedRatherThanMissing()
    {
        var match = PostLinkMatcher.MatchFiles(
            [InFolder("a.png", null), InFolder("b.png", null), InFolder("c.png", "EP.2"), InFolder("d.png", "EP.2"),
             InFolder("e.png", "EP.3")],
            ["a.png"], libraryHas: null);

        Assert.Equal(["b.png"], match.Missing); // the root was started, so its other file is missing
        Assert.Equal(
            [("EP.2", 2), ("EP.3", 1)],
            match.UnstartedFolders.Select(f => (f.Folder!, f.Files)));
        Assert.Equal("https://x/folder/EP.2", match.UnstartedFolders[0].FolderUrl);
    }

    [Fact]
    public void AFolderWithAFileDroppedInIsStartedSoItsRemainderIsMissing()
    {
        var match = PostLinkMatcher.MatchFiles(
            [InFolder("a.png", null), InFolder("c.png", "EP.2"), InFolder("d.png", "EP.2")],
            ["c.png"], libraryHas: null);

        Assert.Equal(["d.png"], match.Missing);
        // The root has nothing dropped, so what it holds is not started.
        Assert.Equal(null, Assert.Single(match.UnstartedFolders).Folder);
    }

    [Fact]
    public void AFolderTheLibraryAlreadyHasSomeOfIsStarted()
    {
        var match = PostLinkMatcher.MatchFiles(
            [InFolder("a.png", null), InFolder("c.png", "EP.2"), InFolder("d.png", "EP.2")],
            ["a.png"], (name, _) => name == "c.png");

        Assert.Equal(["c.png"], match.InLibrary);
        Assert.Equal(["d.png"], match.Missing); // EP.2 was started earlier, so d.png is really missing
        Assert.Empty(match.UnstartedFolders);
    }

    [Fact]
    public void AFlatShareHasNoUnstartedFolders()
    {
        var match = PostLinkMatcher.MatchFiles([new ListedFile("a.png", 1), new ListedFile("b.png", 2)], ["a.png"], null);

        Assert.Equal(["b.png"], match.Missing);
        Assert.Empty(match.UnstartedFolders);
    }

    [Fact]
    public void MatcherWithNothingListedMatchesNothing()
    {
        var match = PostLinkMatcher.Match([], ["a.png"]);

        Assert.Empty(match.Matched);
        Assert.Equal(["a.png"], match.Unlisted);
    }
}

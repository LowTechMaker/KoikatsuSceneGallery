using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// One accidental name clash among a friend's cards used to abort the whole
/// batch, so these cases are about what happens to the rest of the batch — and
/// about not renaming a card whose content the library already has.
/// </summary>
public sealed class LocalDestinationNamesTests
{
    private const string Folder = @"C:\library\characters\friend";

    private sealed class Disk
    {
        /// <summary>Path to content, standing in for the file system.</summary>
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Claimed { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string Resolve(string source, string ideal)
            => LocalDestinationNames.Resolve(
                source,
                ideal,
                Claimed,
                path => Files.ContainsKey(path),
                (left, right) => Content(left) == Content(right));

        private string Content(string path)
            => Files.TryGetValue(path, out var content) ? content : path;
    }

    private static string At(string fileName) => Path.Combine(Folder, fileName);

    [Fact]
    public void AFreeNameIsUsedAsItIs()
    {
        var disk = new Disk();

        Assert.Equal(At("card.png"), disk.Resolve(@"D:\drop\card.png", At("card.png")));
    }

    // The behaviour the transaction relies on: an identical file already in the
    // library is left to the duplicate path, which deletes the source instead
    // of importing a second copy under a new name.
    [Fact]
    public void AnIdenticalCardKeepsTheTakenNameSoItIsFoldedAsADuplicate()
    {
        var disk = new Disk();
        disk.Files[At("card.png")] = "same";
        disk.Files[@"D:\drop\card.png"] = "same";

        Assert.Equal(At("card.png"), disk.Resolve(@"D:\drop\card.png", At("card.png")));
    }

    [Fact]
    public void ADifferentCardWithATakenNameIsSuffixed()
    {
        var disk = new Disk();
        disk.Files[At("card.png")] = "theirs";
        disk.Files[@"D:\drop\card.png"] = "mine";

        Assert.Equal(At("card_1.png"), disk.Resolve(@"D:\drop\card.png", At("card.png")));
    }

    [Fact]
    public void SuffixingSkipsPastEveryTakenName()
    {
        var disk = new Disk();
        disk.Files[At("card.png")] = "a";
        disk.Files[At("card_1.png")] = "b";
        disk.Files[At("card_2.png")] = "c";
        disk.Files[@"D:\drop\card.png"] = "d";

        Assert.Equal(At("card_3.png"), disk.Resolve(@"D:\drop\card.png", At("card.png")));
    }

    // Matching an existing suffixed copy still counts as a duplicate.
    [Fact]
    public void AnIdenticalSuffixedCopyIsMatchedRatherThanAddedTo()
    {
        var disk = new Disk();
        disk.Files[At("card.png")] = "theirs";
        disk.Files[At("card_1.png")] = "mine";
        disk.Files[@"D:\drop\card.png"] = "mine";

        Assert.Equal(At("card_1.png"), disk.Resolve(@"D:\drop\card.png", At("card.png")));
    }

    // Two unrelated cards sharing a name inside one batch: the second must not
    // plan onto the first one's destination, which no file system check would
    // catch because nothing has been moved yet.
    [Fact]
    public void TwoDifferentCardsInOneBatchGetDifferentDestinations()
    {
        var disk = new Disk();
        disk.Files[@"D:\a\card.png"] = "first";
        disk.Files[@"D:\b\card.png"] = "second";

        Assert.Equal(At("card.png"), disk.Resolve(@"D:\a\card.png", At("card.png")));
        Assert.Equal(At("card_1.png"), disk.Resolve(@"D:\b\card.png", At("card.png")));
    }

    // The same card dropped twice: both plans share a destination on purpose,
    // so the executor deletes the second source.
    [Fact]
    public void TheSameCardTwiceInOneBatchSharesOneDestination()
    {
        var disk = new Disk();
        disk.Files[@"D:\a\card.png"] = "same";
        disk.Files[@"D:\b\card.png"] = "same";

        Assert.Equal(At("card.png"), disk.Resolve(@"D:\a\card.png", At("card.png")));
        Assert.Equal(At("card.png"), disk.Resolve(@"D:\b\card.png", At("card.png")));
    }

    [Fact]
    public void ClaimsAreCaseInsensitiveLikeTheFileSystem()
    {
        var disk = new Disk();
        disk.Files[@"D:\a\Card.png"] = "first";
        disk.Files[@"D:\b\card.png"] = "second";

        Assert.Equal(At("Card.png"), disk.Resolve(@"D:\a\Card.png", At("Card.png")));
        Assert.Equal(At("card_1.png"), disk.Resolve(@"D:\b\card.png", At("card.png")));
    }

    [Fact]
    public void AnUnreadableComparisonIsTreatedAsDifferentRatherThanThrowing()
    {
        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var resolved = LocalDestinationNames.Resolve(
            @"D:\drop\card.png",
            At("card.png"),
            claimed,
            path => path == At("card.png"),
            (_, _) => false);

        Assert.Equal(At("card_1.png"), resolved);
    }

    // Every name taken by different content: the plan keeps the ideal path so
    // the executor reports the clash rather than the helper inventing a name.
    [Fact]
    public void ANameThatCannotBeFreedFallsBackToTheIdealPath()
    {
        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var resolved = LocalDestinationNames.Resolve(
            @"D:\drop\card.png",
            At("card.png"),
            claimed,
            _ => true,
            (_, _) => false);

        Assert.Equal(At("card.png"), resolved);
        Assert.Empty(claimed);
    }
}

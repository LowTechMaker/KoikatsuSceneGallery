using System.Runtime.InteropServices;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class LibraryCardMoverTests
{
    private static readonly byte[] Card = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] OtherCard = [8, 7, 6, 5, 4, 3, 2, 1];

    private static LibraryCardMover CrossVolume(LibraryCardMoverHooks? hooks = null)
    {
        hooks ??= new LibraryCardMoverHooks();
        hooks.IsSameVolume = (_, _) => false;
        return new LibraryCardMover(hooks);
    }

    private static LibraryCardMover SameVolume(LibraryCardMoverHooks? hooks = null)
    {
        hooks ??= new LibraryCardMoverHooks();
        hooks.IsSameVolume = (_, _) => true;
        return new LibraryCardMover(hooks);
    }

    public static TheoryData<bool> BothVolumes => new() { true, false };

    private static LibraryCardMover Mover(bool sameVolume, LibraryCardMoverHooks? hooks = null)
        => sameVolume ? SameVolume(hooks) : CrossVolume(hooks);

    /// <summary>Everything under the test folder, so a stray temp file or folder shows up.</summary>
    private static string[] Tree(TestDirectory directory)
        => [.. Directory.EnumerateFileSystemEntries(directory.Path, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(directory.Path, p)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(BothVolumes))]
    public async Task ACardIsMovedIntoANewFolder(bool sameVolume)
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("Unknown", "a.png"), Card);
        var destination = Path.Combine(directory.Path, "author", "post", "a.png");

        var result = await Mover(sameVolume).MoveAsync(source, destination, default);

        Assert.Equal(LibraryCardMoveOutcome.Moved, result.Outcome);
        Assert.Equal(destination, result.FinalPath);
        Assert.Null(result.Error);
        Assert.False(File.Exists(source));
        Assert.Equal(Card, File.ReadAllBytes(destination));
        // No temp file left beside it, and a real file rather than a link.
        Assert.Equal(["Unknown", "author", Path.Combine("author", "post"), Path.Combine("author", "post", "a.png")], Tree(directory));
        Assert.False(File.GetAttributes(destination).HasFlag(FileAttributes.ReparsePoint));
    }

    [Fact]
    public async Task TheRealMoverMovesOnTheSameVolume()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "b", "a.png");

        var result = await new LibraryCardMover().MoveAsync(source, destination, default);

        Assert.Equal(LibraryCardMoveOutcome.Moved, result.Outcome);
        Assert.Equal(Card, File.ReadAllBytes(destination));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task TheSamePathIsAlreadyInPlace()
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("x", "a.png"), Card);
        var spelledOtherwise = Path.Combine(directory.Path, "x", "..", "x", ".", "a.png");

        foreach (var destination in new[] { source, spelledOtherwise })
        {
            var result = await CrossVolume().MoveAsync(source, destination, default);
            Assert.Equal(LibraryCardMoveOutcome.AlreadyInPlace, result.Outcome);
            Assert.Equal(Card, File.ReadAllBytes(source));
        }
        Assert.Equal(["x", Path.Combine("x", "a.png")], Tree(directory));
    }

    [WindowsFact]
    public async Task TheSamePathIsDecidedWithoutOpeningTheFile()
    {
        // A card open exclusively in another program: the same path is in
        // place without needing to read it, rather than a failure.
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        using var hold = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await SameVolume().MoveAsync(source, Path.Combine(directory.Path, ".", "a.png"), default);

        Assert.Equal(LibraryCardMoveOutcome.AlreadyInPlace, result.Outcome);
    }

    [WindowsFact]
    public async Task TheSamePathInAnotherCaseIsAlreadyInPlace()
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("x", "a.png"), Card);

        var result = await SameVolume().MoveAsync(source, Path.Combine(directory.Path, "X", "A.PNG"), default);

        Assert.Equal(LibraryCardMoveOutcome.AlreadyInPlace, result.Outcome);
        Assert.Equal(["x", Path.Combine("x", "a.png")], Tree(directory));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    [WindowsFact]
    public async Task TheSameFileThroughAHardLinkIsAlreadyInPlaceAndBothNamesStay()
    {
        // Another spelling of the very same file. Treating it as a duplicate
        // would invite deleting the "source", which is the destination too.
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "b.png");
        Assert.True(CreateHardLink(destination, source, IntPtr.Zero));

        foreach (var sameVolume in new[] { true, false })
        {
            var result = await Mover(sameVolume).MoveAsync(source, destination, default);
            Assert.Equal(LibraryCardMoveOutcome.AlreadyInPlace, result.Outcome);
        }
        Assert.Equal(Card, File.ReadAllBytes(source));
        Assert.Equal(Card, File.ReadAllBytes(destination));
    }

    [Theory]
    [MemberData(nameof(BothVolumes))]
    public async Task ADifferentFileAtTheDestinationIsACollisionAndNothingIsTouched(bool sameVolume)
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("u", "a.png"), Card);
        // Same length, different bytes: length alone must not decide.
        var destination = directory.Write(Path.Combine("d", "a.png"), OtherCard);

        var result = await Mover(sameVolume).MoveAsync(source, destination, default);

        Assert.Equal(LibraryCardMoveOutcome.Collision, result.Outcome);
        Assert.Equal(Card, File.ReadAllBytes(source));
        Assert.Equal(OtherCard, File.ReadAllBytes(destination));
        Assert.Equal(["d", Path.Combine("d", "a.png"), "u", Path.Combine("u", "a.png")], Tree(directory));
    }

    [Theory]
    [MemberData(nameof(BothVolumes))]
    public async Task AFolderAtTheDestinationIsACollision(bool sameVolume)
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "d", "a.png");
        Directory.CreateDirectory(destination);

        var result = await Mover(sameVolume).MoveAsync(source, destination, default);

        Assert.Equal(LibraryCardMoveOutcome.Collision, result.Outcome);
        Assert.Equal(Card, File.ReadAllBytes(source));
        Assert.True(Directory.Exists(destination));
    }

    [Theory]
    [MemberData(nameof(BothVolumes))]
    public async Task IdenticalBytesAtTheDestinationAreADuplicateAndBothFilesStay(bool sameVolume)
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("u", "a.png"), Card);
        var destination = directory.Write(Path.Combine("d", "a.png"), Card);

        var result = await Mover(sameVolume).MoveAsync(source, destination, default);

        Assert.Equal(LibraryCardMoveOutcome.DuplicateAtDestination, result.Outcome);
        Assert.Equal(destination, result.FinalPath);
        Assert.Equal(Card, File.ReadAllBytes(source));
        Assert.Equal(Card, File.ReadAllBytes(destination));
    }

    [Theory]
    [MemberData(nameof(BothVolumes))]
    public async Task AMissingSourceIsReportedAndNoFolderIsCreated(bool sameVolume)
    {
        using var directory = new TestDirectory();

        var result = await Mover(sameVolume).MoveAsync(
            Path.Combine(directory.Path, "gone.png"), Path.Combine(directory.Path, "d", "gone.png"), default);

        Assert.Equal(LibraryCardMoveOutcome.SourceMissing, result.Outcome);
        Assert.Empty(Tree(directory));
    }

    [Theory]
    [MemberData(nameof(BothVolumes))]
    public async Task AFailureBeforeCommitLeavesTheSourceAndRemovesOnlyTheFoldersItCreated(bool sameVolume)
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("u", "a.png"), Card);
        Directory.CreateDirectory(Path.Combine(directory.Path, "existing"));
        var destination = Path.Combine(directory.Path, "existing", "new1", "new2", "a.png");
        var hooks = new LibraryCardMoverHooks { BeforeCommit = () => throw new IOException("disk full") };

        var result = await Mover(sameVolume, hooks).MoveAsync(source, destination, default);

        Assert.Equal(LibraryCardMoveOutcome.Failed, result.Outcome);
        Assert.Contains("disk full", result.Error);
        Assert.Equal(source, result.FinalPath);
        Assert.Equal(Card, File.ReadAllBytes(source));
        // The folder that was there before stays; the two it made are gone, and so is any temp file.
        Assert.Equal(["existing", "u", Path.Combine("u", "a.png")], Tree(directory));
    }

    [Theory]
    [MemberData(nameof(BothVolumes))]
    public async Task AFolderThatGotContentIsNotRemovedOnFailure(bool sameVolume)
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var created = Path.Combine(directory.Path, "new");
        var destination = Path.Combine(created, "a.png");
        var hooks = new LibraryCardMoverHooks
        {
            BeforeCommit = () =>
            {
                // Someone else put a file in the folder this move just made.
                File.WriteAllBytes(Path.Combine(created, "theirs.png"), OtherCard);
                throw new IOException("boom");
            },
        };

        await Mover(sameVolume, hooks).MoveAsync(source, destination, default);

        Assert.Equal(OtherCard, File.ReadAllBytes(Path.Combine(created, "theirs.png")));
        Assert.Equal(Card, File.ReadAllBytes(source));
    }

    [Theory]
    [MemberData(nameof(BothVolumes))]
    public async Task AFileThatAppearsAtTheDestinationIsNeverOverwritten(bool sameVolume)
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "d", "a.png");
        var hooks = new LibraryCardMoverHooks { BeforeCommit = () => File.WriteAllBytes(destination, OtherCard) };

        var result = await Mover(sameVolume, hooks).MoveAsync(source, destination, default);

        Assert.Equal(LibraryCardMoveOutcome.Collision, result.Outcome);
        Assert.Equal(OtherCard, File.ReadAllBytes(destination));
        Assert.Equal(Card, File.ReadAllBytes(source));
        Assert.Equal(["a.png", "d", Path.Combine("d", "a.png")], Tree(directory));
    }

    [Fact]
    public async Task ACopyThatDoesNotVerifyIsDiscardedAndTheSourceStays()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "d", "a.png");
        string? temp = null;
        var hooks = new LibraryCardMoverHooks
        {
            // Same length, one byte different: the hash must catch it.
            AfterCopy = path => { temp = path; File.WriteAllBytes(path, [.. Card[..^1], 99]); },
        };

        var result = await CrossVolume(hooks).MoveAsync(source, destination, default);

        Assert.Equal(LibraryCardMoveOutcome.Failed, result.Outcome);
        Assert.NotNull(temp);
        Assert.Equal(Path.GetDirectoryName(destination), Path.GetDirectoryName(temp));
        Assert.False(File.Exists(temp));
        Assert.False(File.Exists(destination));
        Assert.Equal(Card, File.ReadAllBytes(source));
        Assert.Equal(["a.png"], Tree(directory));
    }

    [Fact]
    public async Task ATruncatedCopyIsDiscarded()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "d", "a.png");
        var hooks = new LibraryCardMoverHooks { AfterCopy = path => File.WriteAllBytes(path, Card[..4]) };

        var result = await CrossVolume(hooks).MoveAsync(source, destination, default);

        Assert.Equal(LibraryCardMoveOutcome.Failed, result.Outcome);
        Assert.Equal(["a.png"], Tree(directory));
    }

    [Fact]
    public async Task ACopyThatThrowsIsDiscarded()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "d", "a.png");
        var hooks = new LibraryCardMoverHooks { AfterCopy = _ => throw new IOException("network gone") };

        var result = await CrossVolume(hooks).MoveAsync(source, destination, default);

        Assert.Equal(LibraryCardMoveOutcome.Failed, result.Outcome);
        Assert.Contains("network gone", result.Error);
        Assert.Equal(Card, File.ReadAllBytes(source));
        Assert.Equal(["a.png"], Tree(directory));
    }

    [Fact]
    public async Task WhenTheSourceCannotBeDeletedTheCopyIsRemovedAndTheSourceStays()
    {
        // Two copies would look like a duplicate to the next import, and the
        // card's state and rating would point at either; one card, one place.
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "d", "a.png");
        var hooks = new LibraryCardMoverHooks { BeforeSourceDelete = () => throw new IOException("in use") };

        var result = await CrossVolume(hooks).MoveAsync(source, destination, default);

        Assert.Equal(LibraryCardMoveOutcome.Failed, result.Outcome);
        Assert.Equal(source, result.FinalPath);
        Assert.Equal(Card, File.ReadAllBytes(source));
        Assert.Equal(["a.png"], Tree(directory));
    }

    [WindowsFact]
    public async Task ALockedSourceIsNotLostAcrossVolumes()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "d", "a.png");
        FileStream? hold = null;
        // Readers allowed, deleting is not: a viewer that has the card open.
        var hooks = new LibraryCardMoverHooks
        {
            BeforeSourceDelete = () => hold = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read),
        };
        try
        {
            var result = await CrossVolume(hooks).MoveAsync(source, destination, default);
            Assert.Equal(LibraryCardMoveOutcome.Failed, result.Outcome);
        }
        finally
        {
            hold?.Dispose();
        }
        Assert.Equal(Card, File.ReadAllBytes(source));
        Assert.Equal(["a.png"], Tree(directory));
    }

    [Fact]
    public async Task ASourceChangedDuringTheCopyIsNotDeleted()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "d", "a.png");
        var hooks = new LibraryCardMoverHooks
        {
            BeforeSourceDelete = () => File.AppendAllText(source, "edited"),
        };

        var result = await CrossVolume(hooks).MoveAsync(source, destination, default);

        Assert.Equal(LibraryCardMoveOutcome.Failed, result.Outcome);
        Assert.Equal([.. Card, .. "edited"u8.ToArray()], File.ReadAllBytes(source));
        Assert.Equal(["a.png"], Tree(directory));
    }

    [Theory]
    [MemberData(nameof(BothVolumes))]
    public async Task CancellationBeforeStartingTouchesNothing(bool sameVolume)
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Mover(sameVolume)
            .MoveAsync(source, Path.Combine(directory.Path, "d", "a.png"), new CancellationToken(canceled: true)));

        Assert.Equal(["a.png"], Tree(directory));
    }

    [Theory]
    [MemberData(nameof(BothVolumes))]
    public async Task CancellationJustBeforeCommitLeavesEverythingAsItWas(bool sameVolume)
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        using var cancel = new CancellationTokenSource();
        var hooks = new LibraryCardMoverHooks { BeforeCommit = cancel.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Mover(sameVolume, hooks)
            .MoveAsync(source, Path.Combine(directory.Path, "d", "e", "a.png"), cancel.Token));

        Assert.Equal(Card, File.ReadAllBytes(source));
        Assert.Equal(["a.png"], Tree(directory));
    }

    [Fact]
    public async Task CancellationDuringTheCrossVolumeCopyLeavesEverythingAsItWas()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        using var cancel = new CancellationTokenSource();
        var hooks = new LibraryCardMoverHooks { AfterCopy = _ => cancel.Cancel() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CrossVolume(hooks)
            .MoveAsync(source, Path.Combine(directory.Path, "d", "a.png"), cancel.Token));

        Assert.Equal(["a.png"], Tree(directory));
    }

    [WindowsFact]
    public void TheVolumeCheckComparesPathRoots()
    {
        Assert.True(LibraryCardMover.AreOnSameVolume(@"C:\a\b.png", @"c:\x\y.png"));
        Assert.False(LibraryCardMover.AreOnSameVolume(@"C:\a\b.png", @"D:\x\y.png"));
        Assert.False(LibraryCardMover.AreOnSameVolume(@"\\server\share\a.png", @"C:\a.png"));
    }
}

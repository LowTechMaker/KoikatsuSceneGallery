using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class LibraryCardRelocationTests
{
    private static readonly byte[] Card = [1, 2, 3, 4];

    /// <summary>Records every step in order, so a test can say what must not have happened.</summary>
    private sealed class Steps
    {
        public List<string> Log { get; } = [];
        public string? Hash { get; set; } = "hash-a";
        public bool SidecarWorks { get; set; } = true;
        public Exception? FollowThrows { get; set; }
        public LibraryCardMover Mover { get; set; } = new();
        public int FolderRating { get; set; }
        public int? Filed { get; set; }
        public bool FiledWorks { get; set; } = true;
        public bool FiledThrows { get; set; }

        public LibraryCardRelocationSteps Build() => new(
            ComputeHash: (path, _) =>
            {
                Log.Add($"hash {Path.GetFileName(path)} exists={File.Exists(path)}");
                return Hash;
            },
            Mover: Mover,
            WriteSidecar: (path, _) =>
            {
                Log.Add($"sidecar {Path.GetFileName(path)}");
                return SidecarWorks ? Task.CompletedTask : throw new IOException("read-only");
            },
            FollowCard: (path, hash, _) =>
            {
                Log.Add($"follow {Path.GetFileName(path)} {hash}");
                return FollowThrows is { } ex ? Task.FromException(ex) : Task.CompletedTask;
            },
            LibraryChanged: (from, to) => Log.Add($"changed {Path.GetFileName(from)} -> {Path.GetFileName(to)}"),
            FolderRatingOf: _ => FolderRating,
            FiledRatingOf: _ => Filed,
            SetFiledRating: (path, hash, rating, _) =>
            {
                Log.Add($"filed {rating} at {Path.GetFileName(Path.GetDirectoryName(path))} exists={File.Exists(path)}");
                return FiledThrows ? throw new IOException("state file locked") : Task.FromResult(FiledWorks);
            });
    }

    // --- refusing before anything moves ---

    [Fact]
    public void AFileOutsideEveryRootIsRefused()
    {
        using var directory = new TestDirectory();
        var root = Path.Combine(directory.Path, "lib");
        string[] roots = [root];

        Assert.Equal(ReidentifyProblem.NotInLibrary, LibraryCardRelocation.Check(
            Path.Combine(directory.Path, "Downloads", "a.png"), roots, hasPost: true, hasAuthor: false));
        // A sibling whose name only starts with the root's.
        Assert.Equal(ReidentifyProblem.NotInLibrary, LibraryCardRelocation.Check(
            Path.Combine(directory.Path, "lib-old", "a.png"), roots, hasPost: true, hasAuthor: false));
        // The root itself is not a card inside it.
        Assert.Equal(ReidentifyProblem.NotInLibrary, LibraryCardRelocation.Check(
            root, roots, hasPost: true, hasAuthor: false));
        Assert.Null(LibraryCardRelocation.Check(
            Path.Combine(root, "Unknown", "a.png"), roots, hasPost: true, hasAuthor: false));
    }

    [Fact]
    public void NeitherAPostNorAnAuthorIsRefused()
    {
        var root = Path.GetFullPath("lib");
        Assert.Equal(ReidentifyProblem.NeedPostOrAuthor, LibraryCardRelocation.Check(
            Path.Combine(root, "a.png"), [root], hasPost: false, hasAuthor: false));
        Assert.Null(LibraryCardRelocation.Check(Path.Combine(root, "a.png"), [root], hasPost: false, hasAuthor: true));
    }

    [Fact]
    public void ADestinationOutsideEveryRootIsRefused()
    {
        var root = Path.GetFullPath("lib");
        Assert.Equal(ReidentifyProblem.NotInLibrary, LibraryCardRelocation.CheckDestination(
            Path.GetFullPath(Path.Combine("elsewhere", "a.png")), [root]));
        Assert.Null(LibraryCardRelocation.CheckDestination(Path.Combine(root, "x", "a.png"), [root]));
    }

    // --- applying ---

    [Fact]
    public async Task AMoveHashesFirstThenMovesThenWritesTheSidecarThenTheStatesFollow()
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("lib", "Unknown", "a.png"), Card);
        var destination = Path.Combine(directory.Path, "lib", "Pixiv", "author", "a.png");
        var steps = new Steps();

        var result = await LibraryCardRelocation.ApplyAsync(source, destination, writesSidecar: true, steps.Build(), default);

        Assert.Equal(LibraryCardMoveOutcome.Moved, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(Card, File.ReadAllBytes(destination));
        // Hashed while the card was still at its old path; everything after the move uses the new one.
        Assert.Equal(["hash a.png exists=True", "sidecar a.png", "follow a.png hash-a", "changed a.png -> a.png"], steps.Log);
    }

    [Fact]
    public async Task NoSidecarIsWrittenWhenThePlanHasNone()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var steps = new Steps();

        await LibraryCardRelocation.ApplyAsync(source, Path.Combine(directory.Path, "b", "a.png"), writesSidecar: false, steps.Build(), default);

        Assert.DoesNotContain(steps.Log, line => line.StartsWith("sidecar", StringComparison.Ordinal));
        Assert.Contains("follow a.png hash-a", steps.Log);
    }

    [Fact]
    public async Task AFileThatIsNotACardIsNotMoved()
    {
        // Without a hash the user's rating and state could not follow the card.
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "b", "a.png");
        var steps = new Steps { Hash = null };

        var result = await LibraryCardRelocation.ApplyAsync(source, destination, writesSidecar: true, steps.Build(), default);

        Assert.Equal(LibraryCardMoveOutcome.Failed, result.Outcome);
        Assert.True(File.Exists(source));
        Assert.False(Directory.Exists(Path.GetDirectoryName(destination)));
        Assert.Single(steps.Log);
    }

    [Theory]
    [InlineData("Collision")]
    [InlineData("DuplicateAtDestination")]
    [InlineData("AlreadyInPlace")]
    [InlineData("SourceMissing")]
    [InlineData("Failed")]
    public async Task NothingElseHappensUnlessTheCardMoved(string outcomeName)
    {
        var outcome = Enum.Parse<LibraryCardMoveOutcome>(outcomeName);
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("u", "a.png"), Card);
        var destination = Path.Combine(directory.Path, "d", "a.png");
        var steps = new Steps();
        switch (outcome)
        {
            case LibraryCardMoveOutcome.Collision: directory.Write(Path.Combine("d", "a.png"), [9, 9]); break;
            case LibraryCardMoveOutcome.DuplicateAtDestination: directory.Write(Path.Combine("d", "a.png"), Card); break;
            case LibraryCardMoveOutcome.AlreadyInPlace: destination = source; break;
            case LibraryCardMoveOutcome.SourceMissing: steps.Hash = "hash-a"; File.Delete(source); break;
            case LibraryCardMoveOutcome.Failed:
                steps.Mover = new LibraryCardMover(new LibraryCardMoverHooks { BeforeCommit = () => throw new IOException("x") });
                break;
        }

        var result = await LibraryCardRelocation.ApplyAsync(source, destination, writesSidecar: true, steps.Build(), default);

        Assert.Equal(outcome, result.Outcome);
        Assert.DoesNotContain(steps.Log, line => !line.StartsWith("hash", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASidecarThatCannotBeWrittenKeepsTheMoveAndSaysSo()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "b", "a.png");
        var steps = new Steps { SidecarWorks = false };

        var result = await LibraryCardRelocation.ApplyAsync(source, destination, writesSidecar: true, steps.Build(), default);

        Assert.Equal(LibraryCardMoveOutcome.Moved, result.Outcome);
        Assert.Equal(LibraryCardRelocation.SidecarMissing, result.Error);
        Assert.Equal(destination, result.FinalPath);
        Assert.True(File.Exists(destination));
        Assert.False(File.Exists(source));
        // The card is in its new place, so its state still follows it.
        Assert.Contains("follow a.png hash-a", steps.Log);
        Assert.Contains("changed a.png -> a.png", steps.Log);
    }

    [Fact]
    public async Task AFailureToMoveTheStatesDoesNotUndoTheMove()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "b", "a.png");
        var steps = new Steps { FollowThrows = new IOException("state file locked") };

        var result = await LibraryCardRelocation.ApplyAsync(source, destination, writesSidecar: true, steps.Build(), default);

        Assert.Equal(LibraryCardMoveOutcome.Moved, result.Outcome);
        Assert.True(File.Exists(destination));
        Assert.Contains("changed a.png -> a.png", steps.Log);
    }

    [Fact]
    public async Task CancellationAfterTheMoveStillFinishesTheBookkeeping()
    {
        // Past the move the card is in its new place: stopping halfway would
        // leave its sidecar, rating and state behind.
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "b", "a.png");
        using var cancel = new CancellationTokenSource();
        var steps = new Steps();
        var built = steps.Build() with
        {
            Mover = new LibraryCardMover(new LibraryCardMoverHooks { IsSameVolume = (_, _) => true }),
        };
        var inner = built.WriteSidecar;
        built = built with
        {
            WriteSidecar = (path, token) =>
            {
                cancel.Cancel();
                Assert.False(token.IsCancellationRequested);
                return inner(path, token);
            },
        };

        var result = await LibraryCardRelocation.ApplyAsync(source, destination, writesSidecar: true, built, cancel.Token);

        Assert.Equal(LibraryCardMoveOutcome.Moved, result.Outcome);
        Assert.Null(result.Error);
        Assert.Contains("follow a.png hash-a", steps.Log);
    }

    [Fact]
    public async Task CancellationBeforeTheMoveTouchesNothing()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var steps = new Steps();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LibraryCardRelocation.ApplyAsync(
            source, Path.Combine(directory.Path, "b", "a.png"), writesSidecar: true, steps.Build(),
            new CancellationToken(canceled: true)));

        Assert.True(File.Exists(source));
        Assert.Empty(steps.Log);
    }

    // --- the rating the old folder implied ---

    [Fact]
    public async Task TheFolderRatingIsRecordedAtTheOldPathBeforeTheCardLeavesIt()
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("R-18", "a.png"), Card);
        var destination = Path.Combine(directory.Path, "author", "a.png");
        var steps = new Steps { FolderRating = 1 };

        var result = await LibraryCardRelocation.ApplyAsync(source, destination, writesSidecar: false, steps.Build(), default);

        Assert.Equal(LibraryCardMoveOutcome.Moved, result.Outcome);
        // Written at the old path while the file was still there, and before anything else.
        Assert.Equal("filed 1 at R-18 exists=True", steps.Log[1]);
        Assert.StartsWith("hash", steps.Log[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGeneralFolderRecordsNothing()
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("G", "a.png"), Card);
        var steps = new Steps { FolderRating = 0 };

        await LibraryCardRelocation.ApplyAsync(source, Path.Combine(directory.Path, "author", "a.png"), false, steps.Build(), default);

        Assert.DoesNotContain(steps.Log, line => line.StartsWith("filed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnExistingFiledRatingIsNotOverwrittenByTheFolder()
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("R-18", "a.png"), Card);
        var steps = new Steps { FolderRating = 1, Filed = 2 };

        await LibraryCardRelocation.ApplyAsync(source, Path.Combine(directory.Path, "author", "a.png"), false, steps.Build(), default);

        Assert.DoesNotContain(steps.Log, line => line.StartsWith("filed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenTheFolderRatingCannotBeRecordedTheCardDoesNotMove()
    {
        // Moving it anyway would turn an R-18 card into a G one, silently.
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("R-18", "a.png"), Card);
        var destination = Path.Combine(directory.Path, "author", "a.png");
        var steps = new Steps { FolderRating = 1, FiledWorks = false };

        var result = await LibraryCardRelocation.ApplyAsync(source, destination, writesSidecar: true, steps.Build(), default);

        Assert.Equal(LibraryCardMoveOutcome.Failed, result.Outcome);
        Assert.True(File.Exists(source));
        Assert.False(Directory.Exists(Path.GetDirectoryName(destination)));
        Assert.Equal(2, steps.Log.Count);
    }

    [Fact]
    public async Task TheChosenRatingIsRecordedAtTheNewPathAfterTheMove()
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("R-18", "a.png"), Card);
        var destination = Path.Combine(directory.Path, "author", "a.png");
        var steps = new Steps { FolderRating = 1 };

        var result = await LibraryCardRelocation.ApplyAsync(source, destination, writesSidecar: false, steps.Build(), default,
            chosenRating: 0);

        Assert.Equal(LibraryCardMoveOutcome.Moved, result.Outcome);
        Assert.Equal("filed 1 at R-18 exists=True", steps.Log[1]);
        // The user's choice comes last and wins, at the card's new place.
        Assert.Equal("filed 0 at author exists=True", steps.Log[^2]);
        Assert.StartsWith("changed", steps.Log[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoChosenRatingRecordsNothingAfterTheMove()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var steps = new Steps();

        await LibraryCardRelocation.ApplyAsync(source, Path.Combine(directory.Path, "b", "a.png"), false, steps.Build(), default);

        Assert.DoesNotContain(steps.Log, line => line.StartsWith("filed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AChosenRatingThatCannotBeRecordedDoesNotUndoTheMove()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("a.png", Card);
        var destination = Path.Combine(directory.Path, "b", "a.png");
        var steps = new Steps { FiledThrows = true };

        var result = await LibraryCardRelocation.ApplyAsync(source, destination, false, steps.Build(), default, chosenRating: 2);

        Assert.Equal(LibraryCardMoveOutcome.Moved, result.Outcome);
        Assert.True(File.Exists(destination));
    }

    [Fact]
    public async Task NoRatingIsChosenOrRecordedUnlessTheCardMoved()
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("u", "a.png"), Card);
        directory.Write(Path.Combine("d", "a.png"), [9, 9]);
        var steps = new Steps();

        var result = await LibraryCardRelocation.ApplyAsync(
            source, Path.Combine(directory.Path, "d", "a.png"), false, steps.Build(), default, chosenRating: 1);

        Assert.Equal(LibraryCardMoveOutcome.Collision, result.Outcome);
        Assert.DoesNotContain(steps.Log, line => line.StartsWith("filed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AThrowWhileRecordingTheFolderRatingAlsoKeepsTheCardInPlace()
    {
        using var directory = new TestDirectory();
        var source = directory.Write(Path.Combine("R-18", "a.png"), Card);
        var steps = new Steps { FolderRating = 1, FiledThrows = true };

        var result = await LibraryCardRelocation.ApplyAsync(
            source, Path.Combine(directory.Path, "author", "a.png"), false, steps.Build(), default);

        Assert.Equal(LibraryCardMoveOutcome.Failed, result.Outcome);
        Assert.True(File.Exists(source));
    }
}

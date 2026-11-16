using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// <see cref="LibraryLayoutMigration"/>: moving an old five-level library to
/// the three-level layout, and back. Every tree here is generated in a temp
/// folder.
/// </summary>
public sealed partial class LibraryLayoutMigrationTests : IDisposable
{
    private readonly TestDirectory _directory = new();
    private string Root => _directory.Path;
    private string Organized => Path.Combine(Root, "Organized");

    public void Dispose() => _directory.Dispose();

    [GeneratedRegex(@"\((?<id>[\w-]+)\)$")]
    private static partial Regex IdInParentheses();

    private LayoutMigrationOptions Options() => new(
        Roots: [Root],
        ImportSubfolder: "Organized",
        ProviderFolders: ["Pixiv", "Local", "Unknown"],
        UnknownFolderName: "Unknown",
        GameFolderNames: ["Koikatsu", "KoikatsuSunshine"],
        RatingFolderNames: [("G", 0), ("R-18", 1), ("R-18G", 2)],
        ParseAuthorId: (_, name) => IdInParentheses().Match(name) is { Success: true } m ? m.Groups["id"].Value : null);

    /// <summary>Writes a file whose bytes are its own relative path unless <paramref name="content"/> says otherwise.</summary>
    private string File_(string relative, string? content = null)
    {
        var path = Path.Combine([Root, .. relative.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content ?? relative);
        return path;
    }

    private string P(string relative) => Path.Combine([Root, .. relative.Split('/')]);

    /// <summary>Every file with its SHA-256 and every folder, outside the root's own metadata folder.</summary>
    private SortedDictionary<string, string> Snapshot()
    {
        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var own = Path.Combine(Root, ".scenegallery");
        foreach (var entry in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories))
        {
            if (entry.StartsWith(own, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = Path.GetRelativePath(Root, entry);
            snapshot[relative] = Directory.Exists(entry)
                ? "<dir>"
                : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)));
        }
        return snapshot;
    }

    private sealed class Recorder
    {
        public List<(string Path, int Rating)> Ratings { get; } = [];
        public List<string> FilesPresentWhenRatingsWereRecorded { get; } = [];
        public List<IReadOnlyList<(string From, string To)>> Batches { get; } = [];
        public HashSet<string> RefuseRatingFor { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Action<string>? AfterMove { get; set; }
        public DateTimeOffset At { get; set; } = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

        public LayoutMigrationSteps Steps(LibraryCardMover? mover = null) => new(
            Mover: mover ?? new LibraryCardMover(),
            RecordRatings: (ratings, _) =>
            {
                Ratings.AddRange(ratings);
                FilesPresentWhenRatingsWereRecorded.AddRange(ratings.Select(r => r.Path).Where(File.Exists));
                return Task.FromResult<IReadOnlyList<string>>([.. ratings.Select(r => r.Path).Where(RefuseRatingFor.Contains)]);
            },
            PathsMoved: (pairs, _) =>
            {
                Batches.Add([.. pairs]);
                return Task.CompletedTask;
            },
            Now: () => At,
            AfterMove: path => AfterMove?.Invoke(path));
    }

    private static string Sidecar(string post, DateTimeOffset fetchedAt, string title, params string[] names)
        => new JsonObject
        {
            ["schemaVersion"] = 2,
            ["providerId"] = "pixiv",
            ["artworkId"] = post,
            ["authorName"] = "Artist",
            ["authorId"] = "42",
            ["title"] = title,
            ["rating"] = 1,
            ["tags"] = new JsonArray(),
            ["fetchedAt"] = fetchedAt.ToString("O"),
            ["localFileNames"] = new JsonArray([.. names.Select(n => (JsonNode)JsonValue.Create(n)!)]),
        }.ToJsonString();

    // ================================================================ planning

    [Fact]
    public void OnlyTheGameAndRatingPositionsAreStripped()
    {
        var a = File_("Organized/Pixiv/Koikatsu/R-18/Artist (42)/Title (1)/a.png");
        // A folder deeper down that happens to be called "G" is left alone.
        var b = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/G/b.png");
        // A rating folder without a game folder.
        var c = File_("Organized/Pixiv/R-18/Artist (42)/c.png");
        var before = Snapshot();

        var plan = LibraryLayoutMigration.Plan(Options(), default);

        var moves = plan.Moves.ToDictionary(m => m.Source, m => m.Destination);
        Assert.Equal(P("Organized/Pixiv/Artist (42)/Title (1)/a.png"), moves[a]);
        Assert.Equal(P("Organized/Pixiv/Artist (42)/G/b.png"), moves[b]);
        Assert.Equal(P("Organized/Pixiv/Artist (42)/c.png"), moves[c]);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void ANewLayoutLibraryHasNothingToMove()
    {
        File_("Organized/Pixiv/Artist (42)/a.png");
        File_("Organized/Unknown/!unrecognized/b.png");

        var plan = LibraryLayoutMigration.Plan(Options(), default);

        Assert.Empty(plan.Moves);
        Assert.Empty(plan.Stays);
    }

    [Fact]
    public void EveryCardAnOldFolderRatesAboveGeneralIsCarriedToBeFiled()
    {
        var r18 = File_("Organized/Pixiv/Koikatsu/R-18/Artist (42)/a.png");
        var r18g = File_("Organized/Pixiv/KoikatsuSunshine/R-18G/Artist (42)/b.png");
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/c.png");
        File_("Organized/Pixiv/Koikatsu/R-18/Artist (42)/notes.txt");

        var plan = LibraryLayoutMigration.Plan(Options(), default);

        Assert.Equal([(r18, 1), (r18g, 2)], plan.Ratings.OrderBy(r => r.Rating).ToList());
    }

    [Fact]
    public void OneAuthorsOldFoldersMergeIntoTheOneWithMostFiles()
    {
        File_("Organized/Pixiv/Koikatsu/G/Old name (42)/a.png");
        File_("Organized/Pixiv/KoikatsuSunshine/R-18/New name (42)/b.png");
        File_("Organized/Pixiv/KoikatsuSunshine/R-18/New name (42)/c.png");
        File_("Organized/Pixiv/Koikatsu/G/Other (7)/d.png");

        var plan = LibraryLayoutMigration.Plan(Options(), default);

        Assert.All(plan.Moves.Where(m => m.Source.Contains("(42)")),
            m => Assert.StartsWith(P("Organized/Pixiv/New name (42)") + Path.DirectorySeparatorChar, m.Destination));
        Assert.Equal(1, plan.AuthorsMerged);
    }

    [Fact]
    public void ATieIsBrokenByOrdinalName()
    {
        File_("Organized/Pixiv/Koikatsu/G/Zed (42)/a.png");
        File_("Organized/Pixiv/KoikatsuSunshine/G/Abe (42)/b.png");

        var plan = LibraryLayoutMigration.Plan(Options(), default);

        Assert.All(plan.Moves, m => Assert.Contains($"{Path.DirectorySeparatorChar}Abe (42){Path.DirectorySeparatorChar}", m.Destination));
    }

    [Fact]
    public void AnExistingNewLayoutFolderIsTheMergeTarget()
    {
        Directory.CreateDirectory(P("Organized/Pixiv/Current (42)"));
        File_("Organized/Pixiv/Koikatsu/G/Old (42)/a.png");
        File_("Organized/Pixiv/Koikatsu/G/Old (42)/b.png");

        var plan = LibraryLayoutMigration.Plan(Options(), default);

        Assert.All(plan.Moves, m => Assert.StartsWith(P("Organized/Pixiv/Current (42)"), m.Destination));
        Assert.Equal(1, plan.AuthorsMerged);
    }

    [Fact]
    public void UnknownCardsGoToTheSinkAndSubfoldersKeepTheirNames()
    {
        var loose = File_("Organized/Unknown/Koikatsu/a.png");
        var sunk = File_("Organized/Unknown/KoikatsuSunshine/!unrecognized/b.png");
        var named = File_("Organized/Unknown/Koikatsu/someone/c.png");

        var moves = LibraryLayoutMigration.Plan(Options(), default).Moves.ToDictionary(m => m.Source, m => m.Destination);

        Assert.Equal(P("Organized/Unknown/!unrecognized/a.png"), moves[loose]);
        Assert.Equal(P("Organized/Unknown/!unrecognized/b.png"), moves[sunk]);
        Assert.Equal(P("Organized/Unknown/someone/c.png"), moves[named]);
    }

    [Fact]
    public void AFileLooseInAProvidersGameOrRatingFolderStays()
    {
        var loose = File_("Organized/Pixiv/Koikatsu/R-18/x.png");

        var plan = LibraryLayoutMigration.Plan(Options(), default);

        Assert.Empty(plan.Moves);
        Assert.Equal(LayoutStayReason.Loose, Assert.Single(plan.Stays, s => s.Source == loose).Reason);
    }

    [Fact]
    public void FilesOutsideTheKnownProviderFoldersAreCountedNotPlanned()
    {
        File_("loose.png");
        File_("Organized/Weird/Koikatsu/G/x (1)/a.png");
        File_("Mine/Koikatsu/b.png");
        File_("Mine/c.png");

        var plan = LibraryLayoutMigration.Plan(Options(), default);

        Assert.Empty(plan.Moves);
        Assert.Equal(
            [("", 1), ("Mine", 1), (Path.Combine("Mine", "Koikatsu"), 1), (Path.Combine("Organized", "Weird"), 1)],
            plan.OutOfScope.Select(o => (o.Location, o.FileCount)).OrderBy(o => o.Location, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void AnOccupiedDestinationIsDecidedBeforeAnythingMoves()
    {
        var different = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png", "old");
        File_("Organized/Pixiv/Artist (42)/a.png", "new");
        var same = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/b.png", "same");
        File_("Organized/Pixiv/Artist (42)/b.png", "same");

        var plan = LibraryLayoutMigration.Plan(Options(), default);

        Assert.Empty(plan.Moves);
        Assert.Equal(LayoutStayReason.Collision, plan.Stays.Single(s => s.Source == different).Reason);
        Assert.Equal(LayoutStayReason.Duplicate, plan.Stays.Single(s => s.Source == same).Reason);
    }

    [Fact]
    public void TwoSourcesForOnePathMoveOneAndKeepTheOther()
    {
        // KK and KKS folders of one author both hold a.png.
        var first = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png", "one");
        var second = File_("Organized/Pixiv/KoikatsuSunshine/G/Artist (42)/a.png", "two");
        var twin = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/b.png", "twin");
        var twin2 = File_("Organized/Pixiv/KoikatsuSunshine/G/Artist (42)/b.png", "twin");

        var plan = LibraryLayoutMigration.Plan(Options(), default);

        // The source first in ordinal order moves; that is the rule.
        var (moves, keeps) = string.CompareOrdinal(first, second) < 0 ? (first, second) : (second, first);
        Assert.Contains(plan.Moves, m => m.Source == moves);
        Assert.Equal(LayoutStayReason.Collision, plan.Stays.Single(s => s.Source == keeps).Reason);
        var (twinMoves, twinKeeps) = string.CompareOrdinal(twin, twin2) < 0 ? (twin, twin2) : (twin2, twin);
        Assert.Contains(plan.Moves, m => m.Source == twinMoves);
        Assert.Equal(LayoutStayReason.Duplicate, plan.Stays.Single(s => s.Source == twinKeeps).Reason);
    }

    [Fact]
    public void ThePlanReadsAsOneLinePerFile()
    {
        var a = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png");
        var loose = File_("Organized/Pixiv/Koikatsu/G/x.png");

        var text = LibraryLayoutMigration.Plan(Options(), default).ToText();

        Assert.Contains($"{a} -> {P("Organized/Pixiv/Artist (42)/a.png")}", text);
        Assert.Contains($"{loose} stays: Loose", text);
    }

    // ================================================================ applying

    [Fact]
    public async Task ApplyingMovesFilesFilesRatingsFirstAndRemovesEmptyOldFolders()
    {
        var a = File_("Organized/Pixiv/Koikatsu/R-18/Artist (42)/a.png");
        var b = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/Title (1)/b.png");
        var recorder = new Recorder();
        var plan = LibraryLayoutMigration.Plan(Options(), default);

        var result = await LibraryLayoutMigration.ApplyAsync(plan, recorder.Steps(), null, default);

        Assert.Equal(2, result.Moved);
        Assert.False(result.Stopped);
        Assert.True(File.Exists(P("Organized/Pixiv/Artist (42)/a.png")));
        Assert.True(File.Exists(P("Organized/Pixiv/Artist (42)/Title (1)/b.png")));
        // Every rating was recorded while every card was still in its old folder.
        Assert.Equal([a], recorder.FilesPresentWhenRatingsWereRecorded);
        Assert.False(Directory.Exists(P("Organized/Pixiv/Koikatsu")));
        // One batch for the root, with every move.
        Assert.Equal(2, Assert.Single(recorder.Batches).Count);
        Assert.Contains((b, P("Organized/Pixiv/Artist (42)/Title (1)/b.png")), recorder.Batches[0]);
        Assert.Empty(LibraryLayoutMigration.FindUnfinished([Root]));
    }

    [Fact]
    public async Task AFolderWithAnythingLeftInItStays()
    {
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png", "old");
        File_("Organized/Pixiv/Artist (42)/a.png", "new");
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/b.png");

        await LibraryLayoutMigration.ApplyAsync(LibraryLayoutMigration.Plan(Options(), default), new Recorder().Steps(), null, default);

        Assert.True(File.Exists(P("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png")));
        Assert.False(File.Exists(P("Organized/Pixiv/Koikatsu/G/Artist (42)/b.png")));
    }

    [Fact]
    public async Task ACardWhoseRatingCouldNotBeFiledDoesNotMove()
    {
        var a = File_("Organized/Pixiv/Koikatsu/R-18/Artist (42)/a.png");
        var b = File_("Organized/Pixiv/Koikatsu/R-18/Artist (42)/b.png");
        var recorder = new Recorder();
        recorder.RefuseRatingFor.Add(a);

        var result = await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), recorder.Steps(), null, default);

        Assert.True(File.Exists(a));
        Assert.False(File.Exists(b));
        Assert.Equal(LayoutStayReason.RatingNotSaved, result.Stayed.Single(s => s.Source == a).Reason);
    }

    [Fact]
    public async Task AFileChangedSinceThePlanStays()
    {
        var a = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png");
        var gone = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/b.png");
        var plan = LibraryLayoutMigration.Plan(Options(), default);
        File.AppendAllText(a, "edited");
        File.Delete(gone);

        var result = await LibraryLayoutMigration.ApplyAsync(plan, new Recorder().Steps(), null, default);

        Assert.True(File.Exists(a));
        Assert.Equal(LayoutStayReason.Changed, result.Stayed.Single(s => s.Source == a).Reason);
        Assert.Equal(LayoutStayReason.Changed, result.Stayed.Single(s => s.Source == gone).Reason);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public async Task TheMoverDecidesAgainAtTheTimeOfTheMove()
    {
        var a = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png", "mine");
        var plan = LibraryLayoutMigration.Plan(Options(), default);
        var theirs = File_("Organized/Pixiv/Artist (42)/a.png", "theirs");

        var result = await LibraryLayoutMigration.ApplyAsync(plan, new Recorder().Steps(), null, default);

        Assert.Equal("theirs", File.ReadAllText(theirs));
        Assert.Equal("mine", File.ReadAllText(a));
        Assert.Equal(LayoutStayReason.Collision, Assert.Single(result.Stayed).Reason);
    }

    [Fact]
    public async Task StoppingKeepsWhatMovedAndLeavesARunThatCanBeResumed()
    {
        for (var i = 0; i < 5; i++) File_($"Organized/Pixiv/Koikatsu/G/Artist (42)/{i}.png");
        var recorder = new Recorder();
        using var cancel = new CancellationTokenSource();
        var progress = new SynchronousProgress(p => { if (p.Done == 2) cancel.Cancel(); });

        var result = await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), recorder.Steps(), progress, cancel.Token);

        Assert.True(result.Stopped);
        Assert.Equal(2, result.Moved);
        Assert.Equal(2, Assert.Single(recorder.Batches).Count);
        var journal = Assert.Single(LibraryLayoutMigration.FindUnfinished([Root]));

        var resumed = await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), recorder.Steps(), null, default);

        Assert.Equal(3, resumed.Moved);
        Assert.Equal([journal], resumed.Journals);
        Assert.Empty(LibraryLayoutMigration.FindUnfinished([Root]));
    }

    /// <summary>Reports on the calling thread, so a test can cancel at an exact point.</summary>
    private sealed class SynchronousProgress(Action<LayoutMigrationProgress> report) : IProgress<LayoutMigrationProgress>
    {
        public void Report(LayoutMigrationProgress value) => report(value);
    }

    private sealed class Killed : Exception;

    [Fact]
    public async Task ARunKilledInTheMiddleIsResumedAndThenUndoneCompletely()
    {
        for (var i = 0; i < 6; i++) File_($"Organized/Pixiv/Koikatsu/R-18/Artist (42)/{i}.png");
        File_("Organized/Unknown/Koikatsu/u.png");
        var before = Snapshot();
        var recorder = new Recorder();
        var moves = 0;
        // The process dies after the third file moved but before the journal said so.
        recorder.AfterMove = _ => { if (++moves == 3) throw new Killed(); };

        await Assert.ThrowsAsync<Killed>(() => LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), recorder.Steps(), null, default));

        var journal = Assert.Single(LibraryLayoutMigration.FindUnfinished([Root]));
        recorder.AfterMove = null;
        var resumed = await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), recorder.Steps(), null, default);
        Assert.Equal(4, resumed.Moved);
        Assert.Empty(LibraryLayoutMigration.FindUnfinished([Root]));
        Assert.Equal(7, Directory.GetFiles(Organized, "*.png", SearchOption.AllDirectories).Length);

        var undone = await LibraryLayoutMigration.RollbackAsync(journal, recorder.Steps(), null, default);

        // All seven, including the one whose move the journal only wrote ahead.
        Assert.Equal(7, undone.Moved);
        Assert.Empty(undone.Stayed);
        Assert.Equal(before, Snapshot());
    }

    // ================================================================ documents

    [Fact]
    public async Task SidecarsOfOneAuthorAreMergedAndPointOnlyAtFilesThatExist()
    {
        var early = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var late = early.AddDays(30);
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png");
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/.scenegallery/fetched_data/pixiv_1.json",
            Sidecar("1", early, "Old title", "a.png", "gone-already.png"));
        File_("Organized/Pixiv/KoikatsuSunshine/G/Artist (42)/b.png");
        File_("Organized/Pixiv/KoikatsuSunshine/G/Artist (42)/.scenegallery/fetched_data/pixiv_1.json",
            Sidecar("1", late, "New title", "b.png"));

        var plan = LibraryLayoutMigration.Plan(Options(), default);
        var merge = Assert.Single(plan.Merges);
        Assert.Equal(LayoutDocumentKind.Sidecar, merge.Kind);
        await LibraryLayoutMigration.ApplyAsync(plan, new Recorder().Steps(), null, default);

        var merged = JsonNode.Parse(File.ReadAllText(P("Organized/Pixiv/Artist (42)/.scenegallery/fetched_data/pixiv_1.json")))!;
        Assert.Equal("New title", merged["title"]!.GetValue<string>());
        Assert.Equal(["a.png", "b.png"],
            merged["localFileNames"]!.AsArray().Select(n => n!.GetValue<string>()).Order().ToArray());
        Assert.False(Directory.Exists(P("Organized/Pixiv/Koikatsu")));
    }

    [Fact]
    public async Task ASidecarWhoseCardStayedStaysWithIt()
    {
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png", "old");
        File_("Organized/Pixiv/Artist (42)/a.png", "different");
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/b.png");
        var sidecar = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/.scenegallery/fetched_data/pixiv_1.json",
            Sidecar("1", DateTimeOffset.UnixEpoch, "T", "a.png", "b.png"));
        var original = File.ReadAllText(sidecar);

        await LibraryLayoutMigration.ApplyAsync(LibraryLayoutMigration.Plan(Options(), default), new Recorder().Steps(), null, default);

        // a.png stayed (collision), so its sidecar is still beside it, unchanged.
        Assert.Equal(original, File.ReadAllText(sidecar));
        Assert.True(File.Exists(P("Organized/Pixiv/Artist (42)/.scenegallery/fetched_data/pixiv_1.json")));
    }

    [Fact]
    public async Task CharacterNotesAreMergedForTheCardsThatMoved()
    {
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png");
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/.scenegallery/characters.json",
            """{"schemaVersion":1,"cards":{"a.png":{"version":"v1"}}}""");
        File_("Organized/Pixiv/Artist (42)/b.png");
        File_("Organized/Pixiv/Artist (42)/.scenegallery/characters.json",
            """{"schemaVersion":1,"cards":{"b.png":{"version":"v2"}}}""");

        await LibraryLayoutMigration.ApplyAsync(LibraryLayoutMigration.Plan(Options(), default), new Recorder().Steps(), null, default);

        var cards = JsonNode.Parse(File.ReadAllText(P("Organized/Pixiv/Artist (42)/.scenegallery/characters.json")))!["cards"]!.AsObject();
        Assert.Equal("v1", cards["a.png"]!["version"]!.GetValue<string>());
        Assert.Equal("v2", cards["b.png"]!["version"]!.GetValue<string>());
        Assert.False(Directory.Exists(P("Organized/Pixiv/Koikatsu")));
    }

    [Fact]
    public async Task ALocalSourceKeepsItsIdentityDocumentWhenMergedIntoAnExistingFolder()
    {
        File_("Organized/Local/Koikatsu/G/Me (local-1)/a.png");
        File_("Organized/Local/Koikatsu/G/Me (local-1)/.scenegallery/source.json", """{"id":"local-1","displayName":"Me"}""");
        var existing = File_("Organized/Local/Me (local-1)/.scenegallery/source.json", """{"id":"local-1","displayName":"Me (renamed)"}""");

        await LibraryLayoutMigration.ApplyAsync(LibraryLayoutMigration.Plan(Options(), default), new Recorder().Steps(), null, default);

        Assert.Equal("""{"id":"local-1","displayName":"Me (renamed)"}""", File.ReadAllText(existing));
        Assert.True(File.Exists(P("Organized/Local/Me (local-1)/a.png")));
        Assert.False(Directory.Exists(P("Organized/Local/Koikatsu")));
    }

    // ================================================================ rolling back

    [Fact]
    public async Task MigratingAndRollingBackLeavesTheLibraryExactlyAsItWas()
    {
        var early = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        File_("Organized/Pixiv/Koikatsu/G/Old name (42)/Title (1)/a.png");
        File_("Organized/Pixiv/Koikatsu/G/Old name (42)/.scenegallery/fetched_data/pixiv_1.json",
            Sidecar("1", early, "Old", "a.png"));
        File_("Organized/Pixiv/KoikatsuSunshine/R-18/New name (42)/b.png");
        File_("Organized/Pixiv/KoikatsuSunshine/R-18/New name (42)/.scenegallery/fetched_data/pixiv_1.json",
            Sidecar("1", early.AddDays(1), "New", "b.png"));
        File_("Organized/Pixiv/KoikatsuSunshine/R-18/New name (42)/!unrecognized/c.png");
        File_("Organized/Pixiv/KoikatsuSunshine/R-18/New name (42)/.scenegallery/characters.json",
            """{"schemaVersion":1,"cards":{"b.png":{"version":"v2"}}}""");
        File_("Organized/Pixiv/Koikatsu/R-18G/Other (7)/d.png");
        File_("Organized/Pixiv/Koikatsu/R-18/loose.png");
        File_("Organized/Local/Koikatsu/G/Me (local-1)/e.png");
        File_("Organized/Local/Koikatsu/G/Me (local-1)/.scenegallery/source.json", """{"id":"local-1"}""");
        File_("Organized/Unknown/Koikatsu/f.png");
        File_("Organized/Unknown/Koikatsu/someone/g.png");
        File_("Organized/Pixiv/Koikatsu/G/Old name (42)/collide.png", "old");
        File_("Organized/Pixiv/New name (42)/collide.png", "new");
        // A document already at the destination: merged into, then restored from its backup.
        File_("Organized/Pixiv/New name (42)/.scenegallery/characters.json",
            """{"schemaVersion":1,"cards":{"collide.png":{"version":"kept"}}}""");
        Directory.CreateDirectory(P("Organized/Pixiv/Koikatsu/G/Empty (9)"));
        File_("Mine/untouched.png");
        var before = Snapshot();

        var plan = LibraryLayoutMigration.Plan(Options(), default);
        var applied = await LibraryLayoutMigration.ApplyAsync(plan, new Recorder().Steps(), null, default);
        Assert.NotEqual(before, Snapshot());
        var journal = Assert.Single(applied.Journals);

        var undone = await LibraryLayoutMigration.RollbackAsync(journal, new Recorder().Steps(), null, default);

        Assert.Empty(undone.Stayed);
        Assert.Equal(before, Snapshot());
        Assert.Empty(LibraryLayoutMigration.FindUnfinished([Root]));
    }

    [Fact]
    public async Task RollingBackSkipsAFileChangedSinceAndBringsTheRestBack()
    {
        var a = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png");
        var b = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/b.png");
        var applied = await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), new Recorder().Steps(), null, default);
        var movedA = P("Organized/Pixiv/Artist (42)/a.png");
        File.AppendAllText(movedA, "user edit");

        var undone = await LibraryLayoutMigration.RollbackAsync(applied.Journals[0], new Recorder().Steps(), null, default);

        Assert.True(File.Exists(movedA));
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.Equal(LayoutStayReason.Changed, Assert.Single(undone.Stayed).Reason);
    }

    [Fact]
    public async Task RollingBackIntoAnOccupiedPathIsACollision()
    {
        var a = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png", "mine");
        var b = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/b.png");
        var applied = await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), new Recorder().Steps(), null, default);
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png", "someone else's");

        var undone = await LibraryLayoutMigration.RollbackAsync(applied.Journals[0], new Recorder().Steps(), null, default);

        Assert.Equal("someone else's", File.ReadAllText(a));
        Assert.Equal("mine", File.ReadAllText(P("Organized/Pixiv/Artist (42)/a.png")));
        Assert.True(File.Exists(b));
        Assert.Equal(LayoutStayReason.Collision, Assert.Single(undone.Stayed).Reason);
    }

    [Fact]
    public async Task RollingBackRepointsThePathKeyedStoresOnce()
    {
        var a = File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png");
        var applied = await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), new Recorder().Steps(), null, default);
        var recorder = new Recorder();

        await LibraryLayoutMigration.RollbackAsync(applied.Journals[0], recorder.Steps(), null, default);

        Assert.Equal((P("Organized/Pixiv/Artist (42)/a.png"), a), Assert.Single(Assert.Single(recorder.Batches)));
    }

    [Fact]
    public async Task ATornLastJournalLineIsIgnored()
    {
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png");
        var applied = await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), new Recorder().Steps(), null, default);
        File.AppendAllText(applied.Journals[0], "{\"op\":\"mov", Encoding.UTF8);

        Assert.Empty(LibraryLayoutMigration.FindUnfinished([Root]));
        var undone = await LibraryLayoutMigration.RollbackAsync(applied.Journals[0], new Recorder().Steps(), null, default);
        Assert.Equal(1, undone.Moved);
        // The rollback's own lines start after the torn one instead of being glued to it.
        Assert.Contains("{\"op\":\"undoing\"}", File.ReadAllLines(applied.Journals[0]));
    }

    [Fact]
    public async Task ARollbackStoppedHalfwayCanBeRunAgainToTheEnd()
    {
        for (var i = 0; i < 4; i++) File_($"Organized/Pixiv/Koikatsu/G/Artist (42)/{i}.png");
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/.scenegallery/characters.json",
            """{"schemaVersion":1,"cards":{"0.png":{"version":"v1"}}}""");
        File_("Organized/Pixiv/Artist (42)/.scenegallery/characters.json",
            """{"schemaVersion":1,"cards":{"x.png":{"version":"v9"}}}""");
        File_("Organized/Pixiv/Artist (42)/x.png");
        var before = Snapshot();
        var applied = await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), new Recorder().Steps(), null, default);
        using var cancel = new CancellationTokenSource();
        var progress = new SynchronousProgress(p => { if (p.Done == 1) cancel.Cancel(); });

        var first = await LibraryLayoutMigration.RollbackAsync(applied.Journals[0], new Recorder().Steps(), progress, cancel.Token);
        Assert.True(first.Stopped);
        Assert.Equal(1, first.Moved);
        Assert.Equal(applied.Journals, LibraryLayoutMigration.FindUnfinished([Root]));

        var second = await LibraryLayoutMigration.RollbackAsync(applied.Journals[0], new Recorder().Steps(), null, default);

        Assert.Equal(3, second.Moved);
        Assert.Empty(second.Stayed);
        Assert.Equal(before, Snapshot());
        Assert.Empty(LibraryLayoutMigration.FindUnfinished([Root]));
    }

    [Fact]
    public void ARatingNameDeeperDownIsNotARatingFolder()
    {
        // A provider without rating folders: the game is stripped, and an
        // author's own subfolder that happens to be called "R-18" stays.
        var a = File_("Organized/Pixiv/Koikatsu/Artist (42)/R-18/a.png");

        var plan = LibraryLayoutMigration.Plan(Options(), default);

        Assert.Equal(P("Organized/Pixiv/Artist (42)/R-18/a.png"), Assert.Single(plan.Moves).Destination);
        Assert.Empty(plan.Ratings);
        Assert.Equal(a, plan.Moves[0].Source);
    }

    [Fact]
    public async Task AResumedRunRecordsTheMoveThatWasInFlight()
    {
        for (var i = 0; i < 3; i++) File_($"Organized/Pixiv/Koikatsu/G/Artist (42)/{i}.png");
        var recorder = new Recorder();
        var moves = 0;
        recorder.AfterMove = _ => { if (++moves == 2) throw new Killed(); };
        await Assert.ThrowsAsync<Killed>(() => LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), recorder.Steps(), null, default));
        recorder.AfterMove = null;

        var resumed = await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), recorder.Steps(), null, default);

        // One confirmed by the first run, one settled on resume, one moved by the resume.
        var confirmed = File.ReadAllLines(resumed.Journals[0]).Count(line => line.Contains("\"op\":\"moved\""));
        Assert.Equal(3, confirmed);
    }

    [Fact]
    public async Task AKilledRunCanBeUndoneWithoutResumingIt()
    {
        for (var i = 0; i < 4; i++) File_($"Organized/Pixiv/Koikatsu/R-18/Artist (42)/{i}.png");
        var before = Snapshot();
        var recorder = new Recorder();
        var moves = 0;
        recorder.AfterMove = _ => { if (++moves == 2) throw new Killed(); };
        await Assert.ThrowsAsync<Killed>(() => LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), recorder.Steps(), null, default));
        var journal = Assert.Single(LibraryLayoutMigration.FindUnfinished([Root]));

        var undone = await LibraryLayoutMigration.RollbackAsync(journal, new Recorder().Steps(), null, default);

        // Both moved files come back, including the one the journal only wrote ahead.
        Assert.Equal(2, undone.Moved);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task AnUnfinishedRollbackIsNeverResumedAsAMigration()
    {
        for (var i = 0; i < 3; i++) File_($"Organized/Pixiv/Koikatsu/G/Artist (42)/{i}.png");
        var applied = await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), new Recorder().Steps(), null, default);
        using var cancel = new CancellationTokenSource();
        var progress = new SynchronousProgress(p => { if (p.Done == 1) cancel.Cancel(); });
        await LibraryLayoutMigration.RollbackAsync(applied.Journals[0], new Recorder().Steps(), progress, cancel.Token);

        var journal = Assert.Single(LibraryLayoutMigration.FindUnfinished([Root]));
        Assert.True(LibraryLayoutMigration.WasBeingUndone(journal));
        Assert.Empty(LibraryLayoutMigration.Plan(Options(), default).UnfinishedJournals);
    }

    // ================================================================ part 3: undo after closing

    private async Task<LayoutMigrationResult> MigrateAsync(Recorder? recorder = null)
        => await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), (recorder ?? new Recorder()).Steps(), null, default);

    [Fact]
    public async Task AFinishedRunCanStillBeUndoneLaterAndNotAfterwards()
    {
        for (var i = 0; i < 3; i++) File_($"Organized/Pixiv/Koikatsu/G/Artist (42)/{i}.png");
        var recorder = new Recorder();
        var applied = await MigrateAsync(recorder);

        var run = Assert.Single(LibraryLayoutMigration.FindUndoable([Root]));
        Assert.Equal(applied.Journals[0], run.Journal);
        Assert.Equal(Root, run.Root);
        Assert.Equal(recorder.At, run.At);
        Assert.Equal(3, run.Moved);
        // Nothing left to move, yet the run is still there to undo.
        Assert.Empty(LibraryLayoutMigration.Plan(Options(), default).Moves);

        await LibraryLayoutMigration.RollbackAsync(run.Journal, new Recorder().Steps(), null, default);

        Assert.Empty(LibraryLayoutMigration.FindUndoable([Root]));
    }

    [Fact]
    public async Task AStoppedRunIsUndoableWithWhatItMoved()
    {
        for (var i = 0; i < 4; i++) File_($"Organized/Pixiv/Koikatsu/G/Artist (42)/{i}.png");
        using var cancel = new CancellationTokenSource();
        var progress = new SynchronousProgress(p => { if (p.Done == 1) cancel.Cancel(); });
        await LibraryLayoutMigration.ApplyAsync(
            LibraryLayoutMigration.Plan(Options(), default), new Recorder().Steps(), progress, cancel.Token);

        Assert.Equal(1, Assert.Single(LibraryLayoutMigration.FindUndoable([Root])).Moved);
    }

    [Fact]
    public async Task AResumedRunIsOneUndoableRunWithEverythingItMoved()
    {
        for (var i = 0; i < 5; i++) File_($"Organized/Pixiv/Koikatsu/G/Artist (42)/{i}.png");
        var recorder = new Recorder();
        var moves = 0;
        recorder.AfterMove = _ => { if (++moves == 3) throw new Killed(); };
        await Assert.ThrowsAsync<Killed>(() => MigrateAsync(recorder));
        // Killed and not resumed: nothing is offered to undo yet (it is offered to resume).
        Assert.Empty(LibraryLayoutMigration.FindUndoable([Root]));
        recorder.AfterMove = null;
        recorder.At = recorder.At.AddMinutes(5);

        await MigrateAsync(recorder);

        var run = Assert.Single(LibraryLayoutMigration.FindUndoable([Root]));
        Assert.Equal(5, run.Moved);
        Assert.Equal(recorder.At.AddMinutes(-5), run.At);
    }

    [Fact]
    public async Task OnlyTheNewestRunOfARootIsOffered()
    {
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png");
        var recorder = new Recorder();
        await MigrateAsync(recorder);
        File_("Organized/Pixiv/KoikatsuSunshine/R-18/Other (7)/b.png");
        File_("Organized/Pixiv/KoikatsuSunshine/R-18/Other (7)/c.png");
        recorder.At = recorder.At.AddHours(1);
        var newer = await MigrateAsync(recorder);

        var run = Assert.Single(LibraryLayoutMigration.FindUndoable([Root]));
        Assert.Equal(newer.Journals[0], run.Journal);
        Assert.Equal(2, run.Moved);

        // Once the newest is undone, the one before it may be.
        await LibraryLayoutMigration.RollbackAsync(run.Journal, new Recorder().Steps(), null, default);
        Assert.Equal(1, Assert.Single(LibraryLayoutMigration.FindUndoable([Root])).Moved);
    }

    [Fact]
    public async Task AnOlderRunIsNotOfferedWhileANewerOneIsUnfinished()
    {
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png");
        var recorder = new Recorder();
        await MigrateAsync(recorder);
        File_("Organized/Pixiv/Koikatsu/G/Other (7)/b.png");
        File_("Organized/Pixiv/Koikatsu/G/Other (7)/c.png");
        recorder.At = recorder.At.AddHours(1);
        var moves = 0;
        recorder.AfterMove = _ => { if (++moves == 1) throw new Killed(); };
        await Assert.ThrowsAsync<Killed>(() => MigrateAsync(recorder));

        Assert.Empty(LibraryLayoutMigration.FindUndoable([Root]));
    }

    [Fact]
    public async Task TwoRunsInTheSameSecondGetTwoJournals()
    {
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png");
        var recorder = new Recorder();
        var first = await MigrateAsync(recorder);
        File_("Organized/Pixiv/Koikatsu/G/Other (7)/b.png");

        var second = await MigrateAsync(recorder);

        Assert.NotEqual(first.Journals[0], second.Journals[0]);
        Assert.Equal(second.Journals[0], Assert.Single(LibraryLayoutMigration.FindUndoable([Root])).Journal);
    }

    [Fact]
    public async Task EveryRootOffersItsNewestRun()
    {
        var second = Path.Combine(Root, "characters");
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png");
        var c = Path.Combine(second, "Organized", "Pixiv", "Koikatsu", "G", "Artist (42)", "c.png");
        Directory.CreateDirectory(Path.GetDirectoryName(c)!);
        File.WriteAllText(c, "c");
        var options = Options() with { Roots = [Root, second] };

        await LibraryLayoutMigration.ApplyAsync(LibraryLayoutMigration.Plan(options, default), new Recorder().Steps(), null, default);

        var runs = LibraryLayoutMigration.FindUndoable([Root, second]);
        Assert.Equal(new[] { Root, second }.Order(), runs.Select(r => r.Root).Order());
    }

    [Fact]
    public async Task APartlyUndoneRunIsOfferedWithWhatIsStillMoved()
    {
        for (var i = 0; i < 3; i++) File_($"Organized/Pixiv/Koikatsu/G/Artist (42)/{i}.png");
        var applied = await MigrateAsync();
        using var cancel = new CancellationTokenSource();
        var progress = new SynchronousProgress(p => { if (p.Done == 1) cancel.Cancel(); });
        await LibraryLayoutMigration.RollbackAsync(applied.Journals[0], new Recorder().Steps(), progress, cancel.Token);

        Assert.Equal(2, Assert.Single(LibraryLayoutMigration.FindUndoable([Root])).Moved);
    }

    [Fact]
    public async Task ANestedRootsRemovedFoldersBelongToItsOwnJournal()
    {
        // Roots can sit inside one another; undoing the inner root alone must
        // bring back the folders the run removed there.
        var inner = Path.Combine(Root, "characters");
        File_("Organized/Pixiv/Koikatsu/G/Artist (42)/a.png");
        var c = Path.Combine(inner, "Organized", "Pixiv", "Koikatsu", "G", "Artist (42)", "c.png");
        Directory.CreateDirectory(Path.GetDirectoryName(c)!);
        File.WriteAllText(c, "c");
        // Only an empty folder shows it: files moving back recreate their own folders.
        var empty = Path.Combine(inner, "Organized", "Pixiv", "Koikatsu", "G", "Empty (9)");
        Directory.CreateDirectory(empty);
        var options = Options() with { Roots = [Root, inner] };
        await LibraryLayoutMigration.ApplyAsync(LibraryLayoutMigration.Plan(options, default), new Recorder().Steps(), null, default);
        Assert.False(Directory.Exists(empty));
        var innerRun = LibraryLayoutMigration.FindUndoable([inner]).Single();

        await LibraryLayoutMigration.RollbackAsync(innerRun.Journal, new Recorder().Steps(), null, default);

        Assert.True(File.Exists(c));
        Assert.True(Directory.Exists(empty));
        Assert.False(Directory.Exists(P("Organized/Pixiv/Koikatsu")));
    }

    // ================================================================ part 3: counting and re-pointing

    [Fact]
    public void AnAuthorWhoseOldFoldersShareANameIsCountedAsMerged()
    {
        File_("Organized/Pixiv/Koikatsu/R-18G/乙 (5)/a.png");
        File_("Organized/Pixiv/KoikatsuSunshine/G/乙 (5)/b.png");
        // One old folder only: moved, not merged.
        File_("Organized/Pixiv/Koikatsu/G/Solo (6)/c.png");
        File_("Organized/Pixiv/Koikatsu/G/Solo (6)/sub/d.png");

        Assert.Equal(1, LibraryLayoutMigration.Plan(Options(), default).AuthorsMerged);
    }

    [Fact]
    public async Task AResumedRunRepointsWhatTheKilledRunMoved()
    {
        // Killed after the moves but before the stores were re-pointed: the
        // resume must re-point those too, not leave them to the hash fallback.
        for (var i = 0; i < 4; i++) File_($"Organized/Pixiv/Koikatsu/G/Artist (42)/{i}.png");
        var recorder = new Recorder();
        var moves = 0;
        recorder.AfterMove = _ => { if (++moves == 3) throw new Killed(); };
        await Assert.ThrowsAsync<Killed>(() => MigrateAsync(recorder));
        Assert.Empty(recorder.Batches);
        recorder.AfterMove = null;

        await MigrateAsync(recorder);

        var batch = Assert.Single(recorder.Batches);
        Assert.Equal(
            Enumerable.Range(0, 4).Select(i => P($"Organized/Pixiv/Koikatsu/G/Artist (42)/{i}.png")).Order(),
            batch.Select(p => p.From).Order());
    }

    [Fact]
    public async Task ARollbackRunAgainAfterBeingKilledRepointsWhatItAlreadyMovedBack()
    {
        for (var i = 0; i < 3; i++) File_($"Organized/Pixiv/Koikatsu/G/Artist (42)/{i}.png");
        var applied = await MigrateAsync();
        var recorder = new Recorder();
        var moves = 0;
        recorder.AfterMove = _ => { if (++moves == 2) throw new Killed(); };
        await Assert.ThrowsAsync<Killed>(() =>
            LibraryLayoutMigration.RollbackAsync(applied.Journals[0], recorder.Steps(), null, default));
        recorder.AfterMove = null;

        await LibraryLayoutMigration.RollbackAsync(applied.Journals[0], recorder.Steps(), null, default);

        Assert.Equal(3, Assert.Single(recorder.Batches).Count);
    }
}

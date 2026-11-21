using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class CardScanTrackerTests
{
    private static readonly DateTime Written = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Local);
    private static readonly DateTime Created = Written.AddDays(-3);

    private static Dictionary<string, CachedCardEntry> Cache(params string[] paths)
        => paths.ToDictionary(p => p, p => new CachedCardEntry(p, 100, Written.Ticks, 252, 352, null, Created.Ticks),
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void AnUnchangedCachedFileIsSkippedAndCountsAsSeen()
    {
        var tracker = new CardScanTracker(Cache("a.png", "b.png"));

        Assert.True(tracker.SkipUnchanged("a.png", 100, Written, Created));
        Assert.Empty(tracker.Missing(["a.png"]));
        Assert.Equal(1, tracker.Skipped);
    }

    [Fact]
    public void ANewOrChangedFileIsNotSkippedButIsSeenOnceItIsRead()
    {
        var tracker = new CardScanTracker(Cache("a.png"));

        Assert.False(tracker.SkipUnchanged("a.png", 101, Written, Created));
        Assert.False(tracker.SkipUnchanged("new.png", 100, Written, Created));
        // Seen only when the scan actually produced a card for it (an unreadable file is not a card).
        Assert.Equal(["a.png", "new.png"], tracker.Missing(["a.png", "new.png"]).Order());

        tracker.MarkRead("a.png", Created);
        tracker.MarkRead("new.png", Created);
        Assert.Empty(tracker.Missing(["a.png", "new.png"]));
        Assert.Equal(0, tracker.Skipped);
    }

    [Fact]
    public void ACachedCardTheScanDidNotSeeIsMissingSoItIsRemoved()
    {
        // The removal must still work although skipped cards never reach the UI thread.
        var tracker = new CardScanTracker(Cache("kept.png", "gone.png", "changed.png"));
        tracker.SkipUnchanged("kept.png", 100, Written, Created);
        tracker.SkipUnchanged("changed.png", 5, Written, Created);
        tracker.MarkRead("changed.png", Created);

        Assert.Equal(["gone.png"], tracker.Missing(["kept.png", "gone.png", "changed.png"]));
    }

    [Fact]
    public void AChangedFileThatCannotBeReadIsMissing()
    {
        var tracker = new CardScanTracker(Cache("broken.png"));

        Assert.False(tracker.SkipUnchanged("broken.png", 7, Written, Created));

        Assert.Equal(["broken.png"], tracker.Missing(["broken.png"]));
    }

    [Fact]
    public void TheListedCreationTimeIsKeptForEveryCardSeen()
    {
        // An entry written before creation times were stored has none; the listing has it.
        var tracker = new CardScanTracker(Cache("a.png"));
        var listed = Created.AddHours(1);
        tracker.SkipUnchanged("a.png", 100, Written, listed);
        tracker.MarkRead("b.png", Created);

        Assert.Equal(listed, tracker.CreatedOf("A.PNG"));
        Assert.Equal(Created, tracker.CreatedOf("b.png"));
        Assert.Null(tracker.CreatedOf("c.png"));
    }

    [Fact]
    public void ManyWorkersCanReportAtOnce()
    {
        var paths = Enumerable.Range(0, 5000).Select(i => $"c{i}.png").ToArray();
        var tracker = new CardScanTracker(Cache(paths));

        Parallel.ForEach(paths, p => tracker.SkipUnchanged(p, 100, Written, Created));

        Assert.Equal(5000, tracker.Skipped);
        Assert.Empty(tracker.Missing(paths));
    }
}

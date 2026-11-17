using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class CardCacheReuseTests
{
    private static readonly DateTime Written = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Local);

    private static CachedCardEntry Entry(string path, long size = 100, DateTime? written = null, int width = 252, int height = 352)
        => new(path, size, (written ?? Written).Ticks, width, height, null, Written.AddDays(-1).Ticks);

    // ------------------------------------------------------------ does the cache describe the file

    [Fact]
    public void AnUnchangedFileIsDescribedByItsEntry()
        => Assert.True(CardCacheReuse.Describes(Entry("a.png"), 100, Written));

    [Fact]
    public void ANewFileHasNoEntryAndIsOpened()
        => Assert.False(CardCacheReuse.Describes(null, 100, Written));

    [Fact]
    public void AFileWrittenSinceIsOpened()
        => Assert.False(CardCacheReuse.Describes(Entry("a.png"), 100, Written.AddSeconds(1)));

    [Theory]
    [InlineData(99)]
    [InlineData(101)]
    public void AFileThatShrankOrGrewWithTheSameWriteTimeIsOpened(long size)
        // A copy tool or a restore can keep the write time; the size still tells.
        => Assert.False(CardCacheReuse.Describes(Entry("a.png"), size, Written));

    [Theory]
    [InlineData(0, 352)]
    [InlineData(252, 0)]
    public void AnEntryWithoutDimensionsIsNotTrusted(int width, int height)
        => Assert.False(CardCacheReuse.Describes(Entry("a.png", width: width, height: height), 100, Written));

    // ------------------------------------------------------------ on a generated library, on disk

    [Fact]
    public void OnAWarmCacheOnlyNewAndChangedFilesAreOpened()
    {
        // 5,000 small files in a deep tree like a real library, listed the way the
        // scan lists them (FileInfo from a directory enumeration: size and write time
        // come from the listing, no file is opened for them).
        using var directory = new TestDirectory();
        var bytes = TestFiles.Png();
        for (var i = 0; i < 5000; i++)
            directory.Write(Path.Combine("Organized", $"p{i % 5}", $"Author {i % 37} ({i % 37})", $"Post {i % 211}", $"c{i:D4}.png"), bytes);

        var cache = List(directory.Path).ToDictionary(
            f => f.FullName, f => new CachedCardEntry(f.FullName, f.Length, f.LastWriteTime.Ticks, 1, 1, null, f.CreationTime.Ticks),
            StringComparer.OrdinalIgnoreCase);

        // Before: the scan opened every listed file.
        Assert.Equal(5000, List(directory.Path).Count());
        // After, with nothing changed: none.
        Assert.Equal(0, Opened(directory.Path, cache));

        // Two files rewritten (one with a different size), one new, one removed.
        var all = cache.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        File.SetLastWriteTime(all[10], DateTime.Now.AddMinutes(1));
        File.WriteAllBytes(all[20], TestFiles.Png(appended: [1, 2, 3]));
        File.SetLastWriteTime(all[20], new DateTime(cache[all[20]].DateModifiedTicks));
        directory.Write(Path.Combine("Organized", "new", "n.png"), bytes);
        File.Delete(all[30]);

        Assert.Equal(3, Opened(directory.Path, cache));
    }

    private static IEnumerable<FileInfo> List(string root)
        => new DirectoryInfo(root).EnumerateFiles("*.png", SearchOption.AllDirectories);

    private static int Opened(string root, IReadOnlyDictionary<string, CachedCardEntry> cache)
        => List(root).Count(f => !CardCacheReuse.Describes(cache.GetValueOrDefault(f.FullName), f.Length, f.LastWriteTime));

    // ------------------------------------------------------------ does the cache file need writing

    [Fact]
    public void TheSameEntriesNeedNoWrite()
    {
        var a = new Dictionary<string, CachedCardEntry>(StringComparer.OrdinalIgnoreCase) { ["a.png"] = Entry("a.png"), ["b.png"] = Entry("b.png") };
        var b = new Dictionary<string, CachedCardEntry>(StringComparer.OrdinalIgnoreCase) { ["B.PNG"] = Entry("b.png"), ["a.png"] = Entry("a.png") };

        Assert.True(CardCacheReuse.SameEntries(a, b));
    }

    [Fact]
    public void AnyDifferenceNeedsAWrite()
    {
        var a = new Dictionary<string, CachedCardEntry>(StringComparer.OrdinalIgnoreCase) { ["a.png"] = Entry("a.png"), ["b.png"] = Entry("b.png") };

        Assert.False(CardCacheReuse.SameEntries(a, new Dictionary<string, CachedCardEntry>(StringComparer.OrdinalIgnoreCase) { ["a.png"] = Entry("a.png") }));
        Assert.False(CardCacheReuse.SameEntries(a, new Dictionary<string, CachedCardEntry>(StringComparer.OrdinalIgnoreCase) { ["a.png"] = Entry("a.png"), ["c.png"] = Entry("c.png") }));
        Assert.False(CardCacheReuse.SameEntries(a, new Dictionary<string, CachedCardEntry>(StringComparer.OrdinalIgnoreCase) { ["a.png"] = Entry("a.png"), ["b.png"] = Entry("b.png", size: 5) }));
        Assert.False(CardCacheReuse.SameEntries(a, new Dictionary<string, CachedCardEntry>(StringComparer.OrdinalIgnoreCase) { ["a.png"] = Entry("a.png"), ["b.png"] = Entry("b.png") with { ThumbnailPath = "t.jpg" } }));
    }
}

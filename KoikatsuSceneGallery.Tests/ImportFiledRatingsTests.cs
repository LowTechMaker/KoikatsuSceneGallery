using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportFiledRatingsTests
{
    private static readonly string Library = Path.GetFullPath(Path.Combine("lib", "Organized", "pixiv", "Author (42)"));

    [Fact]
    public void EveryCommittedCardRatedAboveGeneralIsFiled()
    {
        var a = Path.Combine(Library, "a.png");
        var b = Path.Combine(Library, "b.png");
        var c = Path.Combine(Library, "c.png");

        var filed = ImportFiledRatings.ForCommitted([a, b, c], [(a, 1), (b, 2), (c, 0)]);

        Assert.Equal([(a, 1), (b, 2)], filed);
    }

    [Fact]
    public void AnItemThatWasNotCommittedIsNotFiled()
    {
        var a = Path.Combine(Library, "a.png");
        var rolledBack = Path.Combine(Library, "b.png");

        Assert.Equal([(a, 1)], ImportFiledRatings.ForCommitted([a], [(a, 1), (rolledBack, 1)]));
    }

    [Fact]
    public void AnItemWithoutADestinationIsIgnored()
    {
        var a = Path.Combine(Library, "a.png");

        Assert.Equal([(a, 2)], ImportFiledRatings.ForCommitted([a], [(null, 1), (a, 2)]));
    }

    [Fact]
    public void PathsAreMatchedAsTheFileSystemDoes()
    {
        var a = Path.Combine(Library, "a.png");
        var spelled = Path.Combine(Library, ".", "a.png");

        var filed = ImportFiledRatings.ForCommitted([a], [(spelled, 1)]);

        Assert.Equal([(a, 1)], filed);
        if (OperatingSystem.IsWindows())
            Assert.Equal([(a, 1)], ImportFiledRatings.ForCommitted([a], [(a.ToUpperInvariant(), 1)]));
    }

    [Fact]
    public void AnOutOfRangeRatingIsNotFiled()
    {
        var a = Path.Combine(Library, "a.png");

        Assert.Empty(ImportFiledRatings.ForCommitted([a], [(a, 7)]));
    }

    [Fact]
    public async Task ARatedImportInTheNewLayoutIsFiledAndReadsBackAsR18()
    {
        // The chain the app runs, minus the UI: the import committed a card to
        // a three-level folder, its rating is filed, a fresh store (the next
        // launch) reads it back, and the gallery's rule makes it R-18 although
        // no folder in its path says so.
        using var directory = new TestDirectory();
        var card = directory.Write(Path.Combine("Organized", "local", "Me (local-1)", "a.png"), [1, 2, 3]);
        var store = new CardStateStore();
        store.EnsureLoaded([directory.Path]);

        var filed = ImportFiledRatings.ForCommitted([card], [(card, SceneRating.R18)]);
        Assert.Equal(0, await store.SetFiledRatingsAsync([.. filed.Select(f => (f.Path, "hash-a", f.Rating))]));

        var nextLaunch = new CardStateStore();
        nextLaunch.EnsureLoaded([directory.Path]);
        var empty = new HashSet<string>(["R-18"], StringComparer.OrdinalIgnoreCase);
        var folder = SceneRating.FolderOrFiled(
            SceneRating.FromFolders(card, empty, new HashSet<string>(["R-18G"], StringComparer.OrdinalIgnoreCase)),
            nextLaunch.TryGetByPath(card)?.FiledRating);
        Assert.Equal(SceneRating.R18, SceneRating.Resolve(folder, platformRating: null, userRating: null, useUserRatings: false));
    }
}

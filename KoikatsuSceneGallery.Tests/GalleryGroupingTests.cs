using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class GalleryGroupingTests
{
    [Fact]
    public void AuthorFolderIdIsNotMistakenForPostId()
    {
        Assert.Equal("post:pixiv:123456", GalleryGrouping.ResolveKey(
            "C:/author (999999)/123456_p0.png", "pixiv", "999999", "123456", "999999"));
        Assert.StartsWith("folder:", GalleryGrouping.ResolveKey(
            "C:/author (999999)/one.png", "pixiv", "999999", null, "999999"));
        Assert.Equal("post:pixiv:123456", GalleryGrouping.ResolveKey(
            "C:/post (123456)/one.png", "pixiv", "123456", "654321", "999999"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(6, 1)]
    public void GroupsOnlyAboveFive(int count, int expected)
    {
        var images = Enumerable.Range(0, count).ToArray();
        var groups = GalleryGrouping.Create(images, _ => "folder:a");
        Assert.Equal(expected, groups.Count);
        Assert.Equal(images, groups.SelectMany(g => g.Members));
    }

    [Fact]
    public void KeepsFirstOccurrenceAndMemberSortOrder()
    {
        string[] images = ["a6", "b1", "a5", "a4", "c1", "a3", "a2", "a1"];
        var groups = GalleryGrouping.Create(images, s => s[..1]);
        Assert.Equal(["a", "b", "c"], groups.Select(g => g.Key));
        Assert.Equal(["a6", "a5", "a4", "a3", "a2", "a1"], groups[0].Members);
    }

    [Fact]
    public void FilteredOrDeletedMembersCanTurnAGroupBackIntoSingles()
    {
        var images = Enumerable.Range(0, 6).ToArray();
        Assert.Single(GalleryGrouping.Create(images, _ => "a"));
        Assert.Equal(5, GalleryGrouping.Create(images.Take(5).ToArray(), _ => "a").Count);
    }

    [Fact]
    public void PostIdentityWinsOverSharedFolderAndIncludesProvider()
    {
        Assert.NotEqual(GalleryGrouping.GetKey("C:/a/one.png", "pixiv", "123"),
            GalleryGrouping.GetKey("C:/a/two.png", "fanbox", "123"));
        Assert.Equal(GalleryGrouping.GetKey("C:/a/one.png", "pixiv", "123"),
            GalleryGrouping.GetKey("C:/b/two.png", "pixiv", "123"));
        Assert.NotEqual(GalleryGrouping.GetKey("C:/a/123456_p0.png"),
            GalleryGrouping.GetKey("C:/a/234567_p0.png"));
    }

    [Fact]
    public void FolderFallbackDoesNotMergeSubfolders()
    {
        Assert.Equal(GalleryGrouping.GetKey("C:/a/one.png"), GalleryGrouping.GetKey("C:/a/two.png"));
        Assert.NotEqual(GalleryGrouping.GetKey("C:/a/one.png"), GalleryGrouping.GetKey("C:/a/sub/two.png"));
    }

    [Fact]
    public void WindowsFolderKeysAreCaseInsensitive()
    {
        var images = Enumerable.Range(0, 6).Select(i => $"C:/{(i % 2 == 0 ? "Folder" : "folder")}/{i}.png").ToArray();
        Assert.Single(GalleryGrouping.Create(images, path => GalleryGrouping.GetKey(path)));
    }
}

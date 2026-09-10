using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

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
    public void UnrecognizedSinkFolderIsNeverGrouped()
    {
        var images = Enumerable.Range(0, 7).Select(i => $"C:/author (999)/!unrecognized/{i}.png").ToArray();
        Assert.Equal(7, GalleryGrouping.Create(images, path => GalleryGrouping.GetKey(path)).Count);
        Assert.Equal("post:pixiv:123456", GalleryGrouping.GetKey("C:/author (999)/!unrecognized/123456_p0.png"));
    }

    [Fact]
    public void WindowsFolderKeysAreCaseInsensitive()
    {
        var images = Enumerable.Range(0, 6).Select(i => $"C:/{(i % 2 == 0 ? "Folder" : "folder")}/{i}.png").ToArray();
        Assert.Single(GalleryGrouping.Create(images, path => GalleryGrouping.GetKey(path)));
    }

    // A privately shared file name can contain anything, including a digit run
    // that the link parser would otherwise read as a remote post id.
    [Fact]
    public void LocalSourceFilenamesAreNeverReadAsRemotePosts()
    {
        const string folder = "C:/Organized/Local/阿明 (local-k7f3q9)";
        var expected = "folder:" + Path.GetDirectoryName(folder + "/x.png");

        Assert.Equal("post:pixiv:123456", GalleryGrouping.GetKey(folder + "/123456_p0.png"));
        Assert.Equal("post:bepisdb:KKSCENE_42", GalleryGrouping.GetKey(folder + "/KKSCENE_42.png"));

        Assert.Equal(
            expected,
            GalleryGrouping.GetKey(folder + "/123456_p0.png", LocalSourceIdentity.ProviderId));
        Assert.Equal(
            expected,
            GalleryGrouping.GetKey(folder + "/KKSCENE_42.png", LocalSourceIdentity.ProviderId));
    }

    // Cards land directly in the source folder, so grouping by folder would
    // collapse the entire source into one tile on its author page.
    [Fact]
    public void CardsDirectlyInTheirOwnSourceFolderAreNotGrouped()
    {
        var images = Enumerable.Range(0, 7)
            .Select(i => $"C:/Organized/Local/阿明 (local-k7f3q9)/{i}23456_p0.png")
            .ToArray();

        var groups = GalleryGrouping.Create(
            images,
            path => GalleryGrouping.ResolveKey(
                path, LocalSourceIdentity.ProviderId, null, null, "local-k7f3q9"));

        Assert.Equal(7, groups.Count);
        Assert.All(groups, group => Assert.Single(group.Members));
    }

    // A subfolder the user made inside a source is a real grouping again.
    [Fact]
    public void CardsInASubfolderOfTheirSourceStillGroup()
    {
        var images = Enumerable.Range(0, 7)
            .Select(i => $"C:/Organized/Local/阿明 (local-k7f3q9)/某個系列/{i}.png")
            .ToArray();

        Assert.Single(GalleryGrouping.Create(
            images,
            path => GalleryGrouping.ResolveKey(
                path, LocalSourceIdentity.ProviderId, null, null, "local-k7f3q9")));
    }

    // The exemption is keyed on the author it belongs to, not on any folder
    // that merely looks like a source folder.
    [Fact]
    public void AnotherSourcesFolderIsStillGrouped()
    {
        var images = Enumerable.Range(0, 7)
            .Select(i => $"C:/Organized/Local/別人 (local-aaabbb)/{i}.png")
            .ToArray();

        Assert.Single(GalleryGrouping.Create(
            images,
            path => GalleryGrouping.ResolveKey(
                path, LocalSourceIdentity.ProviderId, null, null, "local-k7f3q9")));
    }
}

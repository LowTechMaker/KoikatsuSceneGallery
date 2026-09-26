using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class PlatformAvailabilityTests
{
    private static TagCloudProviderGroup Group(string providerId) =>
        new(providerId, [new TagCloudEntry("tag", null, 1, [])]);

    [Fact]
    public void TheBuiltInLocalProviderIsNotAPlatform()
    {
        // It is always registered, so counting it would make every install look
        // like it had a platform.
        Assert.Empty(PlatformAvailability.Online(["local"]));
        Assert.False(PlatformAvailability.HasAny(["local"]));
    }

    [Fact]
    public void InstalledPlatformsAreListedWithoutLocal()
        => Assert.Equal(["pixiv", "bepisdb"], PlatformAvailability.Online(["pixiv", "local", "bepisdb"]));

    [Fact]
    public void DuplicatesAndBlanksAreDropped()
        => Assert.Equal(["pixiv"], PlatformAvailability.Online(["pixiv", "PIXIV", "", "   ", null!]));

    [Fact]
    public void NothingInstalledMeansNoPlatformFeatures()
    {
        Assert.False(PlatformAvailability.HasAny([]));
        Assert.True(PlatformAvailability.HasAny(["fanbox"]));
    }

    [Fact]
    public void TagsOfAnUninstalledPlatformAreNotOffered()
    {
        // The sidecars stay on disk after the plugin goes; the tags must not.
        var groups = PlatformAvailability.RestrictToInstalled(
            [Group("bepisdb"), Group("pixiv")], ["pixiv"]);

        Assert.Equal("pixiv", Assert.Single(groups).ProviderId);
    }

    [Fact]
    public void WithNoPluginsNoTagsSurvive()
        => Assert.Empty(PlatformAvailability.RestrictToInstalled(
            [Group("bepisdb"), Group("pixiv")], []));

    [Fact]
    public void LocalTagsAreNeverOfferedEvenIfTheyExist()
        => Assert.Empty(PlatformAvailability.RestrictToInstalled([Group("local")], ["local"]));

    [Fact]
    public void MatchingIgnoresCase()
        => Assert.Single(PlatformAvailability.RestrictToInstalled([Group("Pixiv")], ["pixiv"]));

    [Fact]
    public void SurvivingGroupsKeepTheirOrder()
    {
        var groups = PlatformAvailability.RestrictToInstalled(
            [Group("bepisdb"), Group("fanbox"), Group("pixiv")], ["pixiv", "bepisdb"]);

        Assert.Equal(["bepisdb", "pixiv"], groups.Select(g => g.ProviderId));
    }
}

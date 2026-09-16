using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.IntegrationTests;

/// <summary>
/// The collection page's grid comes from joining the registry with the author
/// service. The registry has to win on existence: author displays are built by
/// resolving cards, and pruned when they lose their last card, so a source can
/// be real and yet absent from the author service entirely.
/// </summary>
public sealed class LocalSourceTileBuilderTests
{
    private static LocalSourceEntry Source(string id, string name, string? directory = @"C:\lib")
        => new(id, name, directory);

    private static AuthorSummary Summary(
        string providerId,
        string id,
        string name,
        int scenes = 0,
        int characters = 0,
        int coordinates = 0)
        => new(
            new AuthorDisplay(new AuthorKey(providerId, id), name, ""),
            scenes,
            characters,
            coordinates,
            DateTime.MinValue);

    [Fact]
    public void ASourceWithNoCardsStillGetsATile()
    {
        var tiles = LocalSourceTileBuilder.Build([Source("local-k7f3q9", "阿明")], []);

        var tile = Assert.Single(tiles);
        Assert.Equal("local-k7f3q9", tile.Entry.Id);
        Assert.Equal("阿明", tile.Summary.Display.Name);
        Assert.Equal(0, tile.Summary.TotalCount);
        Assert.Equal(LocalSourceIdentity.ProviderId, tile.Summary.Display.Key.ProviderId);
    }

    [Fact]
    public void CountsAreJoinedById()
    {
        var tiles = LocalSourceTileBuilder.Build(
            [Source("local-k7f3q9", "阿明"), Source("local-aaabbb", "小華")],
            [Summary(LocalSourceIdentity.ProviderId, "local-k7f3q9", "阿明", scenes: 3, characters: 2)]);

        Assert.Equal(5, tiles.Single(t => t.Entry.Id == "local-k7f3q9").Summary.TotalCount);
        Assert.Equal(0, tiles.Single(t => t.Entry.Id == "local-aaabbb").Summary.TotalCount);
    }

    // A source that has cards must keep the author service's own display: that
    // instance is the one the gallery cards carry and the detail page matches.
    [Fact]
    public void AnExistingDisplayIsReusedRatherThanSynthesized()
    {
        var summary = Summary(LocalSourceIdentity.ProviderId, "local-k7f3q9", "小明", scenes: 1);

        var tile = Assert.Single(LocalSourceTileBuilder.Build(
            [Source("local-k7f3q9", "阿明")],
            [summary]));

        Assert.Same(summary.Display, tile.Summary.Display);
        Assert.Equal("小明", tile.Summary.Display.Name);
    }

    [Fact]
    public void SummariesFromOtherProvidersAreIgnored()
    {
        var tiles = LocalSourceTileBuilder.Build(
            [Source("local-k7f3q9", "阿明")],
            [Summary("pixiv", "local-k7f3q9", "同名的線上作者", scenes: 9)]);

        Assert.Equal(0, Assert.Single(tiles).Summary.TotalCount);
    }

    [Fact]
    public void AnAuthorWithNoRegistryEntryDoesNotAppear()
    {
        var tiles = LocalSourceTileBuilder.Build(
            [],
            [Summary(LocalSourceIdentity.ProviderId, "local-k7f3q9", "阿明", scenes: 3)]);

        Assert.Empty(tiles);
    }

    [Fact]
    public void TilesAreOrderedByDisplayName()
    {
        var tiles = LocalSourceTileBuilder.Build(
            [Source("local-ccc111", "Carol"), Source("local-aaa222", "Alice"), Source("local-bbb333", "Bob")],
            []);

        Assert.Equal(["Alice", "Bob", "Carol"], tiles.Select(t => t.Summary.Display.Name));
    }

    [Fact]
    public void APendingSourceWithNoFolderStillGetsATile()
    {
        var tile = Assert.Single(LocalSourceTileBuilder.Build(
            [Source("local-k7f3q9", "阿明", directory: null)],
            []));

        Assert.Null(tile.Entry.Directory);
        Assert.Equal("阿明", tile.Summary.Display.Name);
    }
}

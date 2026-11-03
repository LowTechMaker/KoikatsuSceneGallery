using System.Security.Cryptography;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// KoikatsuSceneFeed's hand-off manifest (<c>&lt;Downloads&gt;\.scenefeed\handoff.json</c>) names the post a card
/// came from by the card's SHA-256, for cards whose file name does not say (archives, file hosts).
/// </summary>
public sealed class FeedHandoffManifestTests : IDisposable
{
    private static readonly byte[] Card = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "gallery-handoff", Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void CardBelowTheManifestIsFoundByItsBytes()
    {
        WriteManifest(_root, Sha(Card), "pixiv", "123456789");
        var card = WriteCard(Path.Combine(_root, "pixiv", "1 Author", "scene_archive", "20240101_120000.png"));

        var source = new FeedHandoffManifest().Find(card);

        Assert.Equal(new FeedHandoffSource(
            "pixiv", "123456789", "https://example.test/123456789", "1", "Author"), source);
    }

    [Fact]
    public void CardWithOtherBytesIsNotFound()
    {
        WriteManifest(_root, Sha([9, 9, 9]), "pixiv", "123456789");
        var card = WriteCard(Path.Combine(_root, "pixiv", "a.png"));

        Assert.Null(new FeedHandoffManifest().Find(card));
    }

    [Fact]
    public void CardOutsideAnyFeedFolderIsNotFound()
    {
        var card = WriteCard(Path.Combine(_root, "elsewhere", "a.png"));

        Assert.Null(new FeedHandoffManifest().Find(card));
    }

    [Fact]
    public void FirstSourceWinsWhenTheCardCameFromSeveralPosts()
    {
        File.WriteAllText(ManifestPath(_root), $$"""
            {"format":"scenefeed-handoff","version":1,"files":[{"sha256":"{{Sha(Card)}}","sources":[
              {"providerId":"fanbox","postId":"11"},{"providerId":"pixiv","postId":"22"}]}]}
            """);
        var card = WriteCard(Path.Combine(_root, "a.png"));

        Assert.Equal(new FeedHandoffSource("fanbox", "11"), new FeedHandoffManifest().Find(card));
    }

    [Theory]
    [InlineData("other-format", 1)]
    [InlineData("scenefeed-handoff", 2)]
    public void UnknownFormatOrNewerVersionIsIgnored(string format, int version)
    {
        File.WriteAllText(ManifestPath(_root), $$"""
            {"format":"{{format}}","version":{{version}},"files":[{"sha256":"{{Sha(Card)}}","sources":[{"providerId":"pixiv","postId":"1"}]}]}
            """);
        var card = WriteCard(Path.Combine(_root, "a.png"));

        Assert.Null(new FeedHandoffManifest().Find(card));
    }

    [Fact]
    public void BrokenManifestIsIgnoredNotThrown()
    {
        File.WriteAllText(ManifestPath(_root), "{ not json");
        var card = WriteCard(Path.Combine(_root, "a.png"));

        Assert.Null(new FeedHandoffManifest().Find(card));
    }

    [Fact]
    public void OptionalCreatorFieldsAreReadAndUnknownFieldsAreIgnored()
    {
        File.WriteAllText(ManifestPath(_root), $$"""
            {"format":"scenefeed-handoff","version":1,"extra":true,"files":[{"sha256":"{{Sha(Card)}}","more":1,
              "sources":[{"providerId":"pixiv","postId":"5","postUrl":"https://example.test/5","creatorId":"7","creatorName":"A","new":0}]}]}
            """);
        var card = WriteCard(Path.Combine(_root, "a.png"));

        Assert.Equal(new FeedHandoffSource("pixiv", "5", "https://example.test/5", "7", "A"),
            new FeedHandoffManifest().Find(card));
    }

    [Fact]
    public void PatreonCreatorMetadataIsReturnedForPluginlessGalleryImport()
    {
        File.WriteAllText(ManifestPath(_root), $$"""
            {"format":"scenefeed-handoff","version":1,"files":[{"sha256":"{{Sha(Card)}}","sources":[
              {"providerId":" patreon ","postId":" 123456789 ","postUrl":" https://www.patreon.com/posts/scene-pack-123456789 ",
               "creatorId":" 42 ","creatorName":" Test Creator "}]}]}
            """);
        var card = WriteCard(Path.Combine(_root, "a.png"));

        Assert.Equal(new FeedHandoffSource(
            "patreon", "123456789", "https://www.patreon.com/posts/scene-pack-123456789", "42", "Test Creator"),
            new FeedHandoffManifest().Find(card));
    }

    [Fact]
    public void ManifestRewrittenByFeedIsReadAgain()
    {
        var manifests = new FeedHandoffManifest();
        var card = WriteCard(Path.Combine(_root, "a.png"));
        WriteManifest(_root, Sha([9]), "pixiv", "1");
        Assert.Null(manifests.Find(card));

        WriteManifest(_root, Sha(Card), "pixiv", "2");
        File.SetLastWriteTimeUtc(ManifestPath(_root), DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(new FeedHandoffSource("pixiv", "2", "https://example.test/2", "1", "Author"), manifests.Find(card));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string ManifestPath(string root)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, ".scenefeed")).FullName;
        return Path.Combine(folder, "handoff.json");
    }

    private static void WriteManifest(string root, string sha, string providerId, string postId)
        => File.WriteAllText(ManifestPath(root), $$"""
            {"format":"scenefeed-handoff","version":1,"generatedAt":"2026-10-22T00:00:00+00:00","files":[
              {"sha256":"{{sha}}","sources":[{"providerId":"{{providerId}}","postId":"{{postId}}","postUrl":"https://example.test/{{postId}}","creatorId":"1","creatorName":"Author"}]}]}
            """);

    private static string WriteCard(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Card);
        return path;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

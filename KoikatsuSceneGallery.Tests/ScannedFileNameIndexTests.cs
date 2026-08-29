using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ScannedFileNameIndexTests
{
    private const string AuthorDirectory = @"C:\library\pixiv\R18\作者 (987)";

    [Fact]
    public void Match_DoesNotClaimFilesOwnedByAnotherArtwork()
    {
        var index = new ScannedFileNameIndex();
        index.Record(Path.Combine(AuthorDirectory, "12345", "001.png"), "12345");
        index.Record(Path.Combine(AuthorDirectory, "67890", "001.png"), "67890");

        var match = index.Match("99999", ["001.png"]);

        Assert.Empty(match.Files);
        // Both files belong to other posts, so nothing here is plausibly this
        // sidecar's: it must not be protected from orphan cleanup.
        Assert.False(match.HasAmbiguousName);
    }

    [Fact]
    public void Match_TreatsARepeatedUnownedNameAsAmbiguous()
    {
        var index = new ScannedFileNameIndex();
        index.Record(Path.Combine(AuthorDirectory, "unknown-a", "001.png"), null);
        index.Record(Path.Combine(AuthorDirectory, "unknown-b", "001.png"), null);

        var match = index.Match("99999", ["001.png"]);

        // Neither file is identified by the bare name, so claim neither…
        Assert.Empty(match.Files);
        // …but a local file for this sidecar plausibly exists, so keep it.
        Assert.True(match.HasAmbiguousName);
    }

    [Fact]
    public void Match_ClaimsFilesThatNoArtworkOwns()
    {
        var unowned = Path.Combine(AuthorDirectory, "スクショ.png");
        var index = new ScannedFileNameIndex();
        index.Record(unowned, null);
        index.Record(Path.Combine(AuthorDirectory, "12345", "001.png"), "12345");

        var match = index.Match("99999", ["スクショ.png"]);

        Assert.Equal([unowned], match.Files);
        Assert.False(match.HasAmbiguousName);
    }

    [Fact]
    public void Match_ClaimsTheOnlyUnownedFileEvenWhenOthersOwnTheName()
    {
        var unowned = Path.Combine(AuthorDirectory, "unknown-a", "001.png");
        var index = new ScannedFileNameIndex();
        index.Record(unowned, null);
        index.Record(Path.Combine(AuthorDirectory, "12345", "001.png"), "12345");

        // Ownership rules the other file out, leaving exactly one candidate.
        var match = index.Match("99999", ["001.png"]);

        Assert.Equal([unowned], match.Files);
        Assert.False(match.HasAmbiguousName);
    }

    [Fact]
    public void Match_ClaimsFilesInAnUnidentifiedSubfolder()
    {
        // A manually assigned post whose files live in a folder named after
        // the artwork's title rather than its ID.
        var file = Path.Combine(AuthorDirectory, "作品タイトル", "a.png");
        var index = new ScannedFileNameIndex();
        index.Record(file, null);

        Assert.Equal([file], index.Match("99999", ["a.png"]).Files);
    }

    [Fact]
    public void Match_ClaimsEveryFileItOwnsUnderTheSameName()
    {
        var first = Path.Combine(AuthorDirectory, "12345", "001.png");
        var second = Path.Combine(AuthorDirectory, "12345 (title)", "001.png");
        var index = new ScannedFileNameIndex();
        index.Record(first, "12345");
        index.Record(second, "12345");

        // Ownership is definitive, so a repeated name is not ambiguous here.
        var match = index.Match("12345", ["001.png"]);

        Assert.Equal([first, second], match.Files);
        Assert.False(match.HasAmbiguousName);
    }

    [Fact]
    public void Match_ClaimsItsOwnFiles()
    {
        var file = Path.Combine(AuthorDirectory, "12345", "001.png");
        var index = new ScannedFileNameIndex();
        index.Record(file, "12345");

        Assert.Equal([file], index.Match("12345", ["001.png"]).Files);
        Assert.Equal([file], index.Match("12345", ["001.PNG"]).Files);
    }

    [Fact]
    public void Match_IgnoresUnknownAndEmptyNames()
    {
        var index = new ScannedFileNameIndex();
        index.Record(Path.Combine(AuthorDirectory, "a.png"), null);

        foreach (var names in new[]
                 {
                     Array.Empty<string>(),
                     ["missing.png"],
                     new[] { " " },
                 })
        {
            var match = index.Match("99999", names);
            Assert.Empty(match.Files);
            Assert.False(match.HasAmbiguousName);
        }
    }
}

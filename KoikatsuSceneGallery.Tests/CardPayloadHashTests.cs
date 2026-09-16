using System.Text;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// Identifying a card by the data appended after its PNG rather than by the
/// whole file. The case that prompted it: two files of the same character,
/// three kilobytes apart, which no byte comparison would ever fold together.
/// </summary>
public sealed class CardPayloadHashTests
{
    /// <summary>A 1x1 PNG, the smallest thing GetPngSize will accept.</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");

    /// <summary>
    /// A different PNG of the same size class, standing in for a re-encoded
    /// preview image. Two pixels rather than one, so the bytes differ.
    /// </summary>
    private static readonly byte[] OtherPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGPgEpEDIgYIBQAGTgDxOGMygQAAAABJRU5ErkJggg==");

    private static string Card(TestDirectory directory, string name, byte[] png, string payload)
    {
        var path = Path.Combine(directory.Path, name);
        using var stream = File.Create(path);
        stream.Write(png);
        stream.Write(Encoding.UTF8.GetBytes(payload));
        return path;
    }

    // The whole point: same card data, different preview, so different files.
    [Fact]
    public void TwoFilesWithTheSameCardDataHashTheSameHoweverTheirPreviewsDiffer()
    {
        using var directory = new TestDirectory();
        var first = Card(directory, "a.png", TinyPng, "the same character");
        var second = Card(directory, "b.png", OtherPng, "the same character");

        Assert.NotEqual(
            File.ReadAllBytes(first).Length,
            File.ReadAllBytes(second).Length);
        Assert.Equal(CardPayloadHash.TryCompute(first), CardPayloadHash.TryCompute(second));
        Assert.True(CardPayloadHash.AreSameCard(first, second));
    }

    [Fact]
    public void DifferentCardDataHashesDifferentlyEvenBehindTheSamePreview()
    {
        using var directory = new TestDirectory();
        var first = Card(directory, "a.png", TinyPng, "character one");
        var second = Card(directory, "b.png", TinyPng, "character two");

        Assert.NotEqual(CardPayloadHash.TryCompute(first), CardPayloadHash.TryCompute(second));
        Assert.False(CardPayloadHash.AreSameCard(first, second));
    }

    [Fact]
    public void APngWithNothingAppendedIsNotACardAndMatchesNothing()
    {
        using var directory = new TestDirectory();
        var plain = Path.Combine(directory.Path, "plain.png");
        File.WriteAllBytes(plain, TinyPng);
        var card = Card(directory, "card.png", TinyPng, "payload");

        Assert.Null(CardPayloadHash.TryCompute(plain));
        Assert.False(CardPayloadHash.AreSameCard(plain, card));
        Assert.False(CardPayloadHash.AreSameCard(card, plain));

        // Two files that are both not cards must not read as the same card.
        var another = Path.Combine(directory.Path, "another.png");
        File.WriteAllBytes(another, TinyPng);
        Assert.False(CardPayloadHash.AreSameCard(plain, another));
    }

    [Fact]
    public void AFileThatIsNotAPngIsNotACard()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "notes.png");
        File.WriteAllText(path, "not a png at all");

        Assert.Null(CardPayloadHash.TryCompute(path));
    }

    [Fact]
    public void AMissingFileIsNotACardAndDoesNotThrow()
        => Assert.Null(CardPayloadHash.TryCompute(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png")));

    [Fact]
    public void TheHashIsStableAcrossCalls()
    {
        using var directory = new TestDirectory();
        var card = Card(directory, "a.png", TinyPng, "payload");

        Assert.Equal(CardPayloadHash.TryCompute(card), CardPayloadHash.TryCompute(card));
    }
}

/// <summary>
/// Recognizing the suffix a file system adds when it will not overwrite.
/// </summary>
public sealed class CopySuffixTests
{
    // The name from the reported case.
    [Fact]
    public void TheWindowsParenthesisSuffixIsStripped()
        => Assert.Equal(
            "Koikatu_F_20260201060122285_demolition gun.png",
            CopySuffix.Normalize("Koikatu_F_20260201060122285_demolition gun(1).png"));

    [Theory]
    [InlineData("card (2).png")]
    [InlineData("card(2).png")]
    [InlineData("card - Copy.png")]
    [InlineData("card - Copy (2).png")]
    [InlineData("card - 複製.png")]
    [InlineData("card - 副本.png")]
    [InlineData("card - コピー.png")]
    public void EveryCopyMarkerNormalizesToTheSameName(string fileName)
        => Assert.Equal("card.png", CopySuffix.Normalize(fileName));

    // The defect this pattern was rewritten for: a Koikatsu scene name is a
    // chain of underscore-separated numbers. Treating the last one as a copy
    // marker, and stripping repeatedly, collapsed every scene card in the
    // library into one bucket — and one card's import then hashed all of them.
    [Theory]
    [InlineData("2026_0910_1547_51_330.png")]
    [InlineData("2026_0908_1541_53_249.png")]
    [InlineData("118126644_p39.png")]
    [InlineData("card_2.png")]
    public void ANumericUnderscoreSuffixIsNotACopyMarker(string fileName)
        => Assert.Equal(fileName, CopySuffix.Normalize(fileName));

    // One marker at most, for the same reason: repeated stripping is what ate
    // its way down a name until nothing distinguishing was left.
    [Fact]
    public void OnlyOneMarkerIsStripped()
        => Assert.Equal("card (1).png", CopySuffix.Normalize("card (1)(2).png"));

    [Fact]
    public void ANameWithoutAMarkerIsUnchanged()
        => Assert.Equal("card.png", CopySuffix.Normalize("card.png"));

    // Stripping must not eat the whole name: a file actually called "(1).png"
    // has to stay findable.
    [Fact]
    public void AMarkerThatIsTheWholeNameIsKept()
        => Assert.Equal("(1).png", CopySuffix.Normalize("(1).png"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NoNameNormalizesToNothing(string? fileName)
        => Assert.Equal(string.Empty, CopySuffix.Normalize(fileName));

    [Fact]
    public void CopiesOfOneNameAreRecognizedAndTheSameNameIsNot()
    {
        Assert.True(CopySuffix.AreCopiesOfOneName("card(1).png", "card.png"));
        Assert.True(CopySuffix.AreCopiesOfOneName("card(1).png", "card(2).png"));
        Assert.False(CopySuffix.AreCopiesOfOneName("card.png", "card.png"));
        Assert.False(CopySuffix.AreCopiesOfOneName("card.png", "other.png"));
        Assert.False(CopySuffix.AreCopiesOfOneName(
            "2026_0910_1547_51_330.png", "2026_0910_1547_51_331.png"));
    }

    // The timestamp in a Koikatsu name must survive: it is what makes two
    // exports of one card recognizable in the first place.
    [Fact]
    public void TheExportTimestampIsNotMistakenForACopyMarker()
        => Assert.Equal(
            "Koikatu_F_20260201060122285_demolition gun.png",
            CopySuffix.Normalize("Koikatu_F_20260201060122285_demolition gun.png"));
}

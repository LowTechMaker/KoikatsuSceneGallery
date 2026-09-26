using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Tests;

public class UsageLogFormatTests
{
    private static UsageRecord Sample(int i = 0) => new(
        new DateTimeOffset(2026, 9, 21, 12, 0, i, TimeSpan.FromHours(8)),
        "Browser.Drag",
        $@"C:\lib\card_{i}.png",
        "https://www.pixiv.net/artworks/123456");

    [Fact]
    public void ARecordSurvivesARoundTrip()
    {
        var round = UsageLogFormat.Parse([UsageLogFormat.Serialize(Sample())]);

        Assert.Equal(Sample(), Assert.Single(round));
    }

    [Fact]
    public void ARecordWithNoOriginUrlSurvivesToo()
    {
        var local = Sample() with { OriginUrl = null };

        Assert.Null(Assert.Single(UsageLogFormat.Parse([UsageLogFormat.Serialize(local)])).OriginUrl);
    }

    [Fact]
    public void SerializationStaysOnOneLine()
        => Assert.DoesNotContain('\n', UsageLogFormat.Serialize(Sample()));

    [Fact]
    public void ATornFinalLineIsSkippedRatherThanLosingTheFile()
    {
        var lines = new[] { UsageLogFormat.Serialize(Sample(1)), "{\"At\":\"2026-" };

        Assert.Single(UsageLogFormat.Parse(lines));
    }

    [Fact]
    public void BlankLinesAreIgnored()
        => Assert.Empty(UsageLogFormat.Parse(["", "   ", "\t"]));

    [Fact]
    public void TrimKeepsTheNewestEntries()
    {
        var records = Enumerable.Range(0, 10).Select(Sample).ToArray();

        var kept = UsageLogFormat.Trim(records, max: 3);

        Assert.Equal(3, kept.Count);
        Assert.Equal(records[7], kept[0]);
        Assert.Equal(records[9], kept[2]);
    }

    [Fact]
    public void TrimLeavesAShortLogUntouched()
    {
        var records = Enumerable.Range(0, 3).Select(Sample).ToArray();

        Assert.Same(records, UsageLogFormat.Trim(records, max: 10));
    }
}

using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.IntegrationTests;

// Lives here rather than in KoikatsuSceneGallery.Tests because the service
// depends on CardBase and IAppLogger, which belong to the WinUI app assembly;
// only this project references it.

public class UsageRecordServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ksg-usage-" + Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_directory, "usage_log.jsonl");

    public UsageRecordServiceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private sealed class SilentLogger : IAppLogger
    {
        public List<string> Errors { get; } = [];
        public void LogError(string operation, Exception exception, string? context = null)
            => Errors.Add(operation);
    }

    private const string RemotePath = @"C:\lib\pixiv\987654_p0.png";
    private const string LocalPath = @"C:\lib\Local\my private scene.png";

    /// <summary>
    /// Origin is read from the card's author, so the lookup has to carry cards,
    /// not just paths. A path absent from it resolves to null, which counts as
    /// not-local — the same rule <see cref="CardOriginQuery"/> uses.
    /// </summary>
    private static IReadOnlyDictionary<string, CardBase> NoCards() =>
        new Dictionary<string, CardBase>(StringComparer.OrdinalIgnoreCase);

    private UsageRecordService Create(SilentLogger logger, bool enabled = true) =>
        new(() => enabled, NoCards, logger, LogPath);

    [Fact]
    public async Task NothingIsWrittenWhileRecordModeIsOff()
    {
        var service = Create(new SilentLogger(), enabled: false);

        service.Record([RemotePath], "Browser.Drag");
        await service.DrainAsync();

        Assert.False(File.Exists(LogPath));
        Assert.Empty(await service.ReadAsync());
    }

    [Fact]
    public async Task ADraggedCardIsRecordedWithItsOriginUrl()
    {
        var service = Create(new SilentLogger());

        service.Record([RemotePath], "Browser.Drag");
        await service.DrainAsync();

        var record = Assert.Single(await service.ReadAsync());
        Assert.Equal("Browser.Drag", record.Operation);
        Assert.Equal(RemotePath, record.FilePath);
        Assert.Equal("https://www.pixiv.net/artworks/987654", record.OriginUrl);
    }

    [Fact]
    public async Task ACardWithNoDerivableUrlIsStillRecorded()
    {
        var service = Create(new SilentLogger());

        service.Record([LocalPath], "Browser.Drag");
        await service.DrainAsync();

        Assert.Null(Assert.Single(await service.ReadAsync()).OriginUrl);
    }

    [Fact]
    public async Task BlankPathsAreIgnored()
    {
        var service = Create(new SilentLogger());

        service.Record(["", "   ", null!], "Browser.Drag");
        await service.DrainAsync();

        Assert.Empty(await service.ReadAsync());
    }

    [Fact]
    public async Task EntriesAccumulateAcrossCallsAndComeBackNewestFirst()
    {
        var service = Create(new SilentLogger());

        service.Record([@"C:\lib\a_111111_p0.png"], "Browser.Drag");
        await service.DrainAsync();
        service.Record([@"C:\lib\b_222222_p0.png"], "Detail.CopyFilePath");
        await service.DrainAsync();

        var records = await service.ReadAsync();
        Assert.Equal(2, records.Count);
        Assert.Equal("Detail.CopyFilePath", records[0].Operation);
        Assert.Equal("Browser.Drag", records[1].Operation);
    }

    [Fact]
    public async Task ClearRemovesEverything()
    {
        var service = Create(new SilentLogger());
        service.Record([RemotePath], "Browser.Drag");
        await service.DrainAsync();

        await service.ClearAsync();

        Assert.Empty(await service.ReadAsync());
    }

    [Fact]
    public async Task RecordedFiresOnceAFlushLands()
    {
        var service = Create(new SilentLogger());
        var fired = 0;
        service.Recorded += () => fired++;

        service.Record([RemotePath], "Browser.Drag");
        await service.DrainAsync();

        Assert.True(fired >= 1);
    }

    [Fact]
    public async Task ATornFinalLineDoesNotLoseTheRestOfTheLog()
    {
        var service = Create(new SilentLogger());
        service.Record([RemotePath], "Browser.Drag");
        await service.DrainAsync();
        await File.AppendAllTextAsync(LogPath, "{\"At\":\"2026-" + Environment.NewLine);

        Assert.Single(await service.ReadAsync());
    }

    [Fact]
    public async Task ReadingBeforeAnythingIsRecordedIsEmptyRatherThanAnError()
    {
        var logger = new SilentLogger();
        var service = Create(logger);

        Assert.Empty(await service.ReadAsync());
        Assert.Empty(logger.Errors);
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.IntegrationTests;

/// <summary>
/// The library side of the bulk artwork re-fetch: which cards a platform-wide
/// scan attributes to which artwork, that the scan changes nothing on disk,
/// and what a re-fetch writes back.
/// </summary>
public sealed partial class ArtworkRefetchScanTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("refetch-scan").FullName;
    private readonly string _author;
    private readonly Provider _provider = new();
    private readonly AuthorPostService _service;

    public ArtworkRefetchScanTests()
    {
        // The layout the importer produces: provider / game / rating / author.
        _author = Path.Combine(_root, "Organized", "Pixiv", "Koikatsu", "G", "Artist (123)");
        Directory.CreateDirectory(_author);

        var configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new SettingsService.ConfigData
        {
            FolderPaths = [_root],
        }));
        _service = new AuthorPostService([_provider], [_provider], new SettingsService(configPath), new Logger());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Card(params string[] relative)
    {
        var path = Path.Combine([_author, .. relative]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
        return path;
    }

    private string Sidecar(string artworkId, DateTimeOffset fetchedAt, params string[] fileNames)
    {
        var directory = Path.Combine(_author, PostMetadataStore.MetadataDirectoryName, PostMetadataStore.FetchedDataDirectoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"pixiv_{artworkId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            providerId = "pixiv",
            artworkId,
            authorName = "Artist",
            authorId = "123",
            title = "Old title",
            description = (string?)null,
            rating = 0,
            tags = new[] { new { name = "コイカツ!", translatedName = "Koikatsu!" } },
            fetchedAt,
            localFileNames = fileNames,
        }));
        return path;
    }

    [Fact]
    public async Task EveryWayTheLibraryNamesAnArtworkIsFound()
    {
        // A folder carrying the id: the importer's layout, and how cards with
        // no id in their own names were grouped by hand.
        Card("Title (1001)", "2024_0323_1547_38_566.png");
        Card("Title (1001)", "2024_0326_1949_41_597.png");
        // The id in the file name, loose in the author folder.
        Card("2002_p0.png");
        // A loose file with no id at all, known only from its sidecar.
        Card("autosave_2022_0511.png");
        Sidecar("3003", DateTimeOffset.UtcNow, "autosave_2022_0511.png");
        // Nothing says which artwork this is.
        Card("2022_0901_0324_35_182.png");

        var scan = await _service.ScanProviderPostDataAsync("pixiv", default);

        Assert.Equal(["1001", "2002", "3003"], scan.Posts.Select(p => p.ArtworkId.Id).Order());
        Assert.Equal(2, scan.Posts.Single(p => p.ArtworkId.Id == "1001").LocalFileCount);
        Assert.Equal("2022_0901_0324_35_182.png", Assert.Single(scan.UnassignedImages).FileName);
    }

    [Fact]
    public async Task TheAuthorFolderIsNeverTakenForAnArtwork()
    {
        // "Artist (123)" parses as an artwork folder too. The outermost match
        // is the author, so a loose card must not become artwork 123.
        Card("2022_0901_0324_35_182.png");

        var scan = await _service.ScanProviderPostDataAsync("pixiv", default);

        Assert.Empty(scan.Posts);
    }

    [Fact]
    public async Task ScanningChangesNothingOnDisk()
    {
        // The per-author scan deletes a sidecar whose files are all gone. The
        // re-fetch scan runs before the user has agreed to anything, so it
        // must not.
        Card("Title (1001)", "a.png");
        var orphan = Sidecar("4004", DateTimeOffset.UtcNow, "long_gone.png");

        await _service.ScanProviderPostDataAsync("pixiv", default);

        Assert.True(File.Exists(orphan));
    }

    [Fact]
    public async Task TheScanReportsWhenEachArtworkWasLastFetched()
    {
        var fetched = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
        Card("Title (1001)", "a.png");
        Sidecar("1001", fetched);
        Card("2002_p0.png");

        var scan = await _service.ScanProviderPostDataAsync("pixiv", default);

        Assert.Equal(fetched, scan.Posts.Single(p => p.ArtworkId.Id == "1001").MetadataFetchedAt);
        Assert.Null(scan.Posts.Single(p => p.ArtworkId.Id == "2002").MetadataFetchedAt);
    }

    [Fact]
    public async Task ARefetchOverwritesOldDataAndNamesTheFiles()
    {
        Card("Title (1001)", "a.png");
        Card("Title (1001)", "b.png");
        var sidecar = Sidecar("1001", new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
        _provider.Answer = id => Info(id, "New title", "戀活！");

        var post = Assert.Single((await _service.ScanProviderPostDataAsync("pixiv", default)).Posts);
        Assert.Equal(ArtworkRefreshStatus.Found, await _service.RefreshArtworkDetailAsync(post, default));

        var written = new PostMetadataStore().Read(_author, "pixiv", "1001")!;
        Assert.Equal("New title", written.Title);
        Assert.Equal("戀活！", Assert.Single(written.Tags).TranslatedName);
        Assert.Equal(["a.png", "b.png"], written.LocalFileNames.Order());
        Assert.True(File.Exists(sidecar));
    }

    [Fact]
    public async Task AFailedRefetchLeavesTheOldDataAlone()
    {
        // Deleted or private on the platform: what the import got is all
        // there will ever be, so it must survive.
        Card("Title (1001)", "a.png");
        var sidecar = Sidecar("1001", new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), "a.png");
        var before = File.ReadAllText(sidecar);
        _provider.Answer = _ => null;

        var post = Assert.Single((await _service.ScanProviderPostDataAsync("pixiv", default)).Posts);
        Assert.Equal(ArtworkRefreshStatus.Gone, await _service.RefreshArtworkDetailAsync(post, default));

        Assert.Equal(before, File.ReadAllText(sidecar));
    }

    [Fact]
    public async Task OnlyAPlatformThatOptedInCanBeRefetched()
    {
        Assert.True(_service.CanRefreshArtworks("pixiv"));
        Assert.False(new AuthorPostService([new PlainProvider()], [_provider], new SettingsService(Path.Combine(_root, "none.json")), new Logger())
            .CanRefreshArtworks("pixiv"));
        await Task.CompletedTask;
    }

    // ── The service: back up, run, restore ──────────────────────────────

    private ArtworkRefetchService Service(string? backupRoot = null)
    {
        var controller = DispatcherActivation.Run(Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnDedicatedThread);
        return new ArtworkRefetchService(
            _service,
            controller.DispatcherQueue,
            new Logger(),
            onCompleted: () => { },
            statePath: Path.Combine(_root, "refetch.json"),
            backupRoot: backupRoot ?? Path.Combine(_root, "backups"));
    }

    private static async Task Until(Func<bool> condition)
    {
        // Progress is published through the dispatcher, so it lands a moment
        // after the work that caused it.
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition());
    }

    [Fact]
    public async Task TheBackupIsTakenBeforeTheFirstRequestAndARestoreUndoesTheRun()
    {
        Card("Title (1001)", "a.png");
        var existing = Sidecar("1001", new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), "a.png");
        var before = File.ReadAllText(existing);
        Card("Title (1002)", "b.png");
        var created = new PostMetadataStore().GetSidecarPath(_author, "pixiv", "1002");

        var backupRoot = Path.Combine(_root, "backups");
        var backedUpFirst = true;
        _provider.Answer = id =>
        {
            backedUpFirst &= Directory.EnumerateFiles(backupRoot, "manifest.json", SearchOption.AllDirectories).Any();
            return Info(id, "New title", "戀活！");
        };
        var refetch = Service(backupRoot);

        await refetch.ScanAsync("pixiv", resume: false);
        await Until(() => refetch.Phase == ArtworkRefetchPhase.Scanned);
        refetch.Start("pixiv");
        await Until(() => refetch.Phase == ArtworkRefetchPhase.Completed);

        Assert.True(backedUpFirst);
        Assert.Equal("New title", new PostMetadataStore().Read(_author, "pixiv", "1001")!.Title);
        Assert.True(File.Exists(created));

        var backup = Assert.Single(refetch.Backups("pixiv"));
        Assert.Equal((1, 1), (backup.Saved, backup.Absent));

        await refetch.RestoreAsync(backup);
        await Until(() => refetch.LastRestore is not null);

        Assert.Equal(before, File.ReadAllText(existing));
        Assert.False(File.Exists(created));
        Assert.False(refetch.HasUnfinishedRun("pixiv"));
    }

    [Fact]
    public async Task AMissingOnlyRunFetchesOnlyWhatHasNoDataAndLeavesTheRestByteForByte()
    {
        Card("Title (1001)", "a.png");
        var existing = Sidecar("1001", new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), "a.png");
        var before = File.ReadAllText(existing);
        Card("Title (1002)", "b.png");
        var asked = new List<string>();
        _provider.Answer = id => { asked.Add(id.Id); return Info(id, "Fetched", "戀活！"); };
        var refetch = Service();

        await refetch.ScanAsync("pixiv", resume: false);
        await Until(() => refetch.Phase == ArtworkRefetchPhase.Scanned);
        Assert.Equal(1, refetch.Missing);
        refetch.Start("pixiv", KoikatsuSceneGallery.Helpers.ArtworkRefetchScope.MissingOnly);
        await Until(() => refetch.Phase == ArtworkRefetchPhase.Completed);

        Assert.Equal(["1002"], asked);
        Assert.Equal(before, File.ReadAllText(existing));
        Assert.Equal("Fetched", new PostMetadataStore().Read(_author, "pixiv", "1002")!.Title);
    }

    [Fact]
    public async Task AStreakOfDeletedArtworksNeitherStopsTheRunNorIsAskedAboutAgain()
    {
        // What the first real run hit: a resumed missing-only run retries the
        // artworks that never got data first — the deleted ones — and stopped
        // itself on twenty of them in a row.
        for (var id = 5000; id < 5025; id++)
            Card($"Gone ({id})", "x.png");
        Card("Title (9999)", "a.png");
        var asked = new List<string>();
        _provider.Answer = id => { asked.Add(id.Id); return id.Id == "9999" ? Info(id, "Here", "x") : null; };
        var refetch = Service();

        await refetch.ScanAsync("pixiv", resume: false);
        await Until(() => refetch.Phase == ArtworkRefetchPhase.Scanned);
        refetch.Start("pixiv", KoikatsuSceneGallery.Helpers.ArtworkRefetchScope.MissingOnly);
        await Until(() => refetch.Phase is ArtworkRefetchPhase.Completed or ArtworkRefetchPhase.Stopped);

        Assert.Equal(ArtworkRefetchPhase.Completed, refetch.Phase);
        Assert.Equal((1, 25, 0), (refetch.Updated, refetch.Gone, refetch.Failed));

        // A fresh service reads what the last one learned from disk.
        asked.Clear();
        var next = Service();
        await next.ScanAsync("pixiv", resume: false);
        await Until(() => next.Phase == ArtworkRefetchPhase.Scanned);
        Assert.Equal((25, 0), (next.KnownGone, next.Missing));
        next.Start("pixiv", KoikatsuSceneGallery.Helpers.ArtworkRefetchScope.All);
        await Until(() => next.Phase == ArtworkRefetchPhase.Completed);
        Assert.Equal(["9999"], asked);
    }

    [Fact]
    public async Task AStreakOfRealFailuresStillStopsTheRun()
    {
        for (var id = 5000; id < 5000 + KoikatsuSceneGallery.Helpers.ArtworkRefetchPolicy.MaxConsecutiveFailures + 5; id++)
            Card($"Work ({id})", "x.png");
        _provider.Fail = true;
        var refetch = Service();

        await refetch.ScanAsync("pixiv", resume: false);
        await Until(() => refetch.Phase == ArtworkRefetchPhase.Scanned);
        refetch.Start("pixiv", KoikatsuSceneGallery.Helpers.ArtworkRefetchScope.MissingOnly);
        await Until(() => refetch.Phase == ArtworkRefetchPhase.Stopped);

        Assert.Equal(ArtworkRefetchStopReason.TooManyFailures, refetch.StopReason);
        Assert.Equal(KoikatsuSceneGallery.Helpers.ArtworkRefetchPolicy.MaxConsecutiveFailures, refetch.Failed);
        Assert.Equal(0, refetch.Gone);
    }

    [Fact]
    public async Task ARunThatCannotBeBackedUpSendsNothing()
    {
        Card("Title (1001)", "a.png");
        var calls = 0;
        _provider.Answer = id => { calls++; return Info(id, "New", "x"); };
        // A file where the backup folder should go: creating it fails.
        var blocked = Path.Combine(_root, "not-a-folder");
        File.WriteAllText(blocked, "");
        var refetch = Service(blocked);

        await refetch.ScanAsync("pixiv", resume: false);
        await Until(() => refetch.Phase == ArtworkRefetchPhase.Scanned);
        refetch.Start("pixiv");
        await Until(() => refetch.Phase == ArtworkRefetchPhase.Stopped);

        Assert.Equal(ArtworkRefetchStopReason.BackupFailed, refetch.StopReason);
        Assert.Equal(0, calls);
    }

    private static ArtworkInfo Info(ArtworkId id, string title, string translation)
        => new(id, "Artist", "123", title, null, ContentRating.AllAges,
            [new ArtworkTag("コイカツ!", translation)], DateTimeOffset.UtcNow, IsSavedLocally: true);

    [GeneratedRegex(@"^(?<name>.+) \((?<id>\d+)\)$")]
    private static partial Regex NameWithId();

    [GeneratedRegex(@"^(?<id>\d+)_p\d+")]
    private static partial Regex IdInFileName();

    private class PlainProvider : ICardImportProvider, IFolderAuthorProvider, IImportDestinationProvider
    {
        public string Name => "Fake pixiv";
        public string Version => "1.0";
        public string ProviderId => "pixiv";
        public string DestinationFolderName => "Pixiv";
        public bool UsesRatingFolders => true;
        public void Initialize(IPluginHost host) { }

        public ParsedAuthor? TryParseFolderName(string folderName)
            => NameWithId().Match(folderName) is { Success: true } m
                ? new ParsedAuthor(new AuthorKey(ProviderId, m.Groups["id"].Value), m.Groups["name"].Value)
                : null;

        public Task<AuthorInfo?> GetAuthorInfoAsync(AuthorKey key, bool forceRefresh, CancellationToken ct)
            => Task.FromResult<AuthorInfo?>(null);

        public string GetProfileUrl(AuthorKey key) => "";

        public ArtworkId? TryParseFilename(string fileName)
            => IdInFileName().Match(fileName) is { Success: true } m ? new ArtworkId(ProviderId, m.Groups["id"].Value) : null;

        public ArtworkId? TryParseArtworkFolderName(string folderName)
            => NameWithId().Match(folderName) is { Success: true } m ? new ArtworkId(ProviderId, m.Groups["id"].Value) : null;

        public Task<ArtworkInfo?> FetchArtworkInfoAsync(ArtworkId id, CancellationToken ct, bool saveToLocalCache = true)
            => Task.FromResult<ArtworkInfo?>(null);

        public string GetArtworkUrl(ArtworkId id) => "";
    }

    private sealed class Provider : PlainProvider, IArtworkMetadataRefresher
    {
        /// <summary>The artwork, or null for "deleted or private".</summary>
        public Func<ArtworkId, ArtworkInfo?> Answer { get; set; } = _ => null;

        /// <summary>When set, every request fails instead of being answered.</summary>
        public bool Fail { get; set; }

        public Task<ArtworkRefreshResult> RefreshArtworkAsync(ArtworkId id, CancellationToken ct)
            => Task.FromResult(Fail
                ? ArtworkRefreshResult.Failed
                : Answer(id) is { } info ? ArtworkRefreshResult.Found(info) : ArtworkRefreshResult.Gone);
    }

    private sealed class Logger : IAppLogger
    {
        public void LogError(string operation, Exception exception, string? path = null) { }
    }
}

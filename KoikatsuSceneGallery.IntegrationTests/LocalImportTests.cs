using System.Collections.ObjectModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.IntegrationTests;

/// <summary>
/// The load-bearing guarantee of local import: a batch declared local makes no
/// outbound request, even when the file names look exactly like a remote
/// provider's, and lands in a folder that keeps the source's identity.
/// </summary>
public sealed class LocalImportTests
{
    private const string LocalSourceId = "local-k7f3q9";
    private const string LocalSourceName = "阿明";

    private sealed class Logger : IAppLogger
    {
        public List<string> Errors { get; } = [];

        public void LogError(string operation, Exception exception, string? path = null)
            => Errors.Add(operation);
    }

    /// <summary>
    /// Stands in for an installed provider whose file-name pattern a privately
    /// shared card happens to match, and records every fetch attempt.
    /// </summary>
    private sealed class CountingProvider :
        ICardImportProvider, IImportDestinationProvider, IFolderAuthorProvider
    {
        public int FetchCount;
        public int FilenameParseCount;

        public string Name => "Counting provider";
        public string Version => "1.0";
        public string ProviderId => "counting";
        public string DestinationFolderName => "Counting";
        public bool UsesRatingFolders => true;

        public void Initialize(IPluginHost host) { }

        public ArtworkId? TryParseFilename(string fileName)
        {
            Interlocked.Increment(ref FilenameParseCount);
            return new ArtworkId(ProviderId, Path.GetFileNameWithoutExtension(fileName));
        }

        public ArtworkId? TryParseArtworkFolderName(string folderName) => null;

        public string GetArtworkUrl(ArtworkId id) => "https://invalid.example/" + id.Id;

        public Task<ArtworkInfo?> FetchArtworkInfoAsync(
            ArtworkId id, CancellationToken ct, bool saveToLocalCache = true)
        {
            Interlocked.Increment(ref FetchCount);
            return Task.FromResult<ArtworkInfo?>(new ArtworkInfo(
                id, "Remote author", "42", "Title", null,
                ContentRating.R18, [], DateTimeOffset.UtcNow, false));
        }

        public ParsedAuthor? TryParseFolderName(string folderName) => null;

        public string GetProfileUrl(AuthorKey key) => "https://invalid.example/author/" + key.Id;

        public Task<AuthorInfo?> GetAuthorInfoAsync(AuthorKey key, bool forceRefresh, CancellationToken ct)
            => Task.FromResult<AuthorInfo?>(null);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "ksg-local-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Library);

            Config = new SettingsService.ConfigData
            {
                CharacterFolderPaths = [Library],
                ArtworkSubfolderThreshold = 99,
            };

            Registry = new LocalSourceRegistry(Logger);
            Registry.UpdateConfiguration([Library], Config.ImportSubfolder, Config.LocalFolderName);
            LocalProvider = new LocalSourceProvider(Registry);

            // Registration order mirrors the app: installed plugins first, the
            // built-in local provider appended last.
            Service = new ImportService(
                [Remote, LocalProvider],
                [Remote, LocalProvider],
                null,
                () => Task.FromResult(Config),
                Logger,
                new PostMetadataStore(),
                new LibraryFileCache());
        }

        public string Root { get; }
        public string Library => Path.Combine(Root, "library");
        public SettingsService.ConfigData Config { get; }
        public CountingProvider Remote { get; } = new();
        public LocalSourceRegistry Registry { get; }
        public LocalSourceProvider LocalProvider { get; }
        public Logger Logger { get; } = new();
        public ObservableCollection<ImportItem> Items { get; } = [];
        public ImportService Service { get; }

        /// <summary>Publishes synchronously; these tests assert results, not step order.</summary>
        public static bool Publish(Action action)
        {
            action();
            return true;
        }

        /// <summary>Writes a file the remote provider's pattern would match.</summary>
        public string Card(string name)
        {
            var path = Path.Combine(Root, name + ".png");
            using var stream = File.Create(path);
            stream.Write(Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
            using var writer = new BinaryWriter(stream);
            writer.Write(100);
            writer.Write("KoiKatuChara");
            return path;
        }

        public Task<int> AnalyzeLocal(params string[] paths)
            => Service.AnalyzeAsync(paths, Items, Publish, default,
                new ImportAnalysisOptions(new LocalSourceAssignment(LocalSourceId, LocalSourceName)));

        public Task<int> AnalyzeOnline(params string[] paths)
            => Service.AnalyzeAsync(paths, Items, Publish, default);

        public void Dispose()
        {
            Service.CancelPendingResolution();
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task LocalBatch_MakesNoOutboundRequestForARemoteLookingFilename()
    {
        using var f = new Fixture();

        await f.AnalyzeLocal(f.Card("123456_p0"), f.Card("KKSCENE_42"));

        Assert.Equal(0, f.Remote.FetchCount);
        Assert.Equal(0, f.Remote.FilenameParseCount);
        Assert.All(f.Items, item => Assert.Null(item.ArtworkId));
    }

    // Guards the comparison: the same file name does get fetched in an ordinary
    // batch, so the test above is measuring the mode and not a broken fixture.
    [Fact]
    public async Task OnlineBatch_StillFetchesTheSameFilename()
    {
        using var f = new Fixture();

        await f.AnalyzeOnline(f.Card("123456_p0"));

        Assert.Equal(1, f.Remote.FetchCount);
        Assert.NotNull(Assert.Single(f.Items).ArtworkId);
    }

    [Fact]
    public async Task LocalBatch_AssignsTheChosenSourceAndIsReadyToImport()
    {
        using var f = new Fixture();

        await f.AnalyzeLocal(f.Card("123456_p0"));

        var item = Assert.Single(f.Items);
        Assert.Equal(LocalSourceIdentity.ProviderId, item.AuthorProviderId);
        Assert.Equal(LocalSourceId, item.AuthorId);
        Assert.Equal(LocalSourceName, item.AuthorName);
        Assert.Equal(ImportItemStatus.ReadyToImport, item.Status);
        Assert.True(item.HasAuthor);
    }

    // The sink is for files that have an author but no artwork identity, which
    // is every local card; landing there would also cost them folder grouping.
    [Fact]
    public async Task LocalBatch_LandsInTheSourceFolderAndNotTheUnrecognizedSink()
    {
        using var f = new Fixture();

        await f.AnalyzeLocal(f.Card("123456_p0"));

        var destination = Assert.Single(f.Items).DestinationPath;
        Assert.NotNull(destination);
        Assert.DoesNotContain(GalleryGrouping.UnrecognizedFolderName, destination);
        Assert.Equal(
            Path.Combine(
                f.Library,
                f.Config.ImportSubfolder,
                f.Config.LocalFolderName,
                f.Config.KoikatsuFolderName,
                f.Config.GFolderName,
                $"{LocalSourceName} ({LocalSourceId})",
                "123456_p0.png"),
            destination);
    }

    // Rating folders stay on for local sources, because the gallery derives
    // R-18 from the path.
    [Fact]
    public async Task LocalBatch_HonoursTheRatingTheUserPicks()
    {
        using var f = new Fixture();
        await f.AnalyzeLocal(f.Card("card"));

        Assert.Single(f.Items).Rating = ContentRating.R18;
        await f.Service.ReResolveWithDetailedDiagnosticsAsync(f.Items, Fixture.Publish, default);

        Assert.Contains(
            Path.Combine(f.Config.LocalFolderName, f.Config.KoikatsuFolderName, f.Config.R18FolderName),
            Assert.Single(f.Items).DestinationPath);
    }

    // The folder name carries the identity, so a second import of the same
    // source must reuse it rather than create a sibling.
    [Fact]
    public async Task SecondBatchForTheSameSourceReusesItsFolder()
    {
        using var f = new Fixture();
        await f.AnalyzeLocal(f.Card("first"));
        var firstDirectory = Path.GetDirectoryName(Assert.Single(f.Items).DestinationPath);
        Directory.CreateDirectory(firstDirectory!);
        f.Items.Clear();

        await f.AnalyzeLocal(f.Card("second"));

        Assert.Equal(firstDirectory, Path.GetDirectoryName(Assert.Single(f.Items).DestinationPath));
    }

    // The description must be recorded by the import itself, not by a test
    // calling the store directly: without it the library carries no name of
    // its own and depends on this machine's settings.
    [Fact]
    public async Task ImportRecordsTheSourceDescriptionInTheDestinationFolder()
    {
        using var f = new Fixture();
        await f.AnalyzeLocal(f.Card("card"));

        var targets = LocalSourceTargets.Collect(f.Items);

        var target = Assert.Single(targets);
        Assert.Equal(LocalSourceId, target.Id);
        Assert.Equal(LocalSourceName, target.DisplayName);
        Assert.Equal(
            Path.GetDirectoryName(Assert.Single(f.Items).DestinationPath),
            target.Directory);

        await f.Registry.EnsureDocumentAsync(target.Directory, target.Id, target.DisplayName);
        Assert.Equal(
            LocalSourceName,
            new LocalSourceStore().Read(target.Directory)?.DisplayName);
    }

    [Fact]
    public async Task ImportedFolderBecomesAResolvableSourceWithItsRecordedName()
    {
        using var f = new Fixture();
        await f.AnalyzeLocal(f.Card("card"));
        var directory = Path.GetDirectoryName(Assert.Single(f.Items).DestinationPath)!;
        Directory.CreateDirectory(directory);

        await f.Registry.EnsureDocumentAsync(directory, LocalSourceId, LocalSourceName);
        await f.Registry.RescanAsync();

        var parsed = f.LocalProvider.TryParseFolderName(Path.GetFileName(directory));
        Assert.NotNull(parsed);
        Assert.Equal(LocalSourceId, parsed.Key.Id);

        var info = await f.LocalProvider.GetAuthorInfoAsync(parsed.Key, false, CancellationToken.None);
        Assert.Equal(LocalSourceName, info?.Name);
        Assert.Equal("", info?.ProfileUrl);
    }

    [Fact]
    public async Task LocalBatch_ReportsNonCardsWithoutTouchingTheProvider()
    {
        using var f = new Fixture();
        var plain = Path.Combine(f.Root, "plain.png");
        await File.WriteAllBytesAsync(plain, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));

        var rejected = await f.AnalyzeLocal(plain);

        Assert.Equal(1, rejected);
        Assert.Empty(f.Items);
        Assert.Equal(0, f.Remote.FetchCount);
    }
}

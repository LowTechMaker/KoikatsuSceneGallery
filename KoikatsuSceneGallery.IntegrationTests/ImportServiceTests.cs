using System.Collections.ObjectModel;
using System.Threading.Channels;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.IntegrationTests;

public sealed class ImportServiceTests
{
    // Single-artwork analysis publishes these steps in order. Keep that protocol here only.
    public enum Publication { Add, Metadata, Ready, Snapshot, Destination }

    private sealed class Queue
    {
        private readonly Channel<Action> _actions = Channel.CreateUnbounded<Action>();
        public bool Enqueue(Action action) => _actions.Writer.TryWrite(action);
        public async Task<Action> Next()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            return await _actions.Reader.ReadAsync(timeout.Token);
        }
        public async Task Run() => (await Next())();
        public async Task<Action> HoldAnalysisAt(Publication publication)
        {
            for (var step = Publication.Add; step < publication; step++) await Run();
            return await Next();
        }
        public Func<Action, bool> RejectAnalysisAt(Publication publication)
        {
            var next = -1;
            return action => Interlocked.Increment(ref next) != (int)publication && Enqueue(action);
        }
        public async Task DrainUntil(Task task, CancellationToken cancellation = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            while (!task.IsCompleted)
            {
                var available = _actions.Reader.WaitToReadAsync(timeout.Token).AsTask();
                await Task.WhenAny(task, available).WaitAsync(timeout.Token);
                if (_actions.Reader.TryRead(out var action)) action();
            }
            await task;
        }
    }

    private sealed class Provider : ICardImportProvider, IImportDestinationProvider, IFolderAuthorProvider
    {
        public string Name => "Test provider";
        public string Version => "1.0";
        public string ProviderId => "test";
        public string DestinationFolderName => "test";
        public bool UsesRatingFolders => true;
        public Func<ArtworkId, CancellationToken, Task<ArtworkInfo?>> Fetch { get; set; } =
            (id, _) => Task.FromResult<ArtworkInfo?>(Info(id));
        public void Initialize(IPluginHost host) { }
        public Func<string, ArtworkId?> Parse { get; set; } = name => new("test", Path.GetFileNameWithoutExtension(name));
        public ArtworkId? TryParseFilename(string fileName) => Parse(fileName);
        public Func<string, ArtworkId?> ParseArtworkFolder { get; set; } = name =>
            name.EndsWith(")") && name.Contains('(') ? new("test", name[(name.LastIndexOf('(') + 1)..^1]) : null;
        public ArtworkId? TryParseArtworkFolderName(string name) => ParseArtworkFolder(name);
        public string GetArtworkUrl(ArtworkId id) => "https://invalid.example/" + id.Id;
        public ParsedAuthor? TryParseFolderName(string name) =>
            name.EndsWith("(42)") ? new(new(ProviderId, "42"), name) : null;
        public string GetProfileUrl(AuthorKey key) => "https://invalid.example/author/" + key.Id;
        public Task<AuthorInfo?> GetAuthorInfoAsync(AuthorKey key, bool forceRefresh, CancellationToken ct) =>
            Task.FromResult<AuthorInfo?>(null);
        public Task<ArtworkInfo?> FetchArtworkInfoAsync(ArtworkId id, CancellationToken token, bool saveToLocalCache = true) => Fetch(id, token);
    }

    private sealed class Logger : IAppLogger
    {
        public List<string> Errors { get; } = [];
        public void LogError(string operation, Exception exception, string? path = null) => Errors.Add(operation);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new();
        private readonly List<Task> _operations = [];
        private readonly List<Action> _release = [];
        private readonly List<CancellationTokenSource> _linked = [];
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "SceneGallery-Integration-" + Guid.NewGuid());
        public string Library => Path.Combine(Root, "library");
        public SettingsService.ConfigData Config { get; }
        public Provider Provider { get; } = new();
        public Logger Logger { get; } = new();
        public Queue Ui { get; } = new();
        public ObservableCollection<ImportItem> Items { get; } = [];
        public ImportService Service { get; }
        public Func<Task<SettingsService.ConfigData>> LoadConfig { get; set; }
        public Fixture(Func<Task<SettingsService.ConfigData>>? load = null)
        {
            Directory.CreateDirectory(Library);
            Config = new() { CharacterFolderPaths = [Library], ArtworkSubfolderThreshold = 99 };
            LoadConfig = load ?? (() => Task.FromResult(Config));
            Service = new([Provider], [Provider], null, () => LoadConfig(), Logger,
                new PostMetadataStore(), new LibraryFileCache());
        }
        public string Card(string name, bool card = true)
        {
            var path = Path.Combine(Root, name + ".png");
            using var stream = File.Create(path);
            stream.Write(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
            if (card)
            {
                using var writer = new BinaryWriter(stream);
                writer.Write(100);
                writer.Write("KoiKatuChara");
            }
            return path;
        }
        public TaskCompletionSource<T> Gate<T>()
        {
            var gate = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _release.Add(() => gate.TrySetCanceled());
            return gate;
        }
        public void ReleaseOnCleanup(Action release) => _release.Add(release);
        private T Track<T>(T task) where T : Task { _operations.Add(task); return task; }
        public Task<int> Analyze(params string[] paths) => Analyze(paths, default);
        public Task<int> Analyze(string[] paths, CancellationToken token, Func<Action, bool>? enqueue = null)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            _linked.Add(linked);
            return Track(Service.AnalyzeAsync(paths, Items, enqueue ?? Ui.Enqueue, linked.Token));
        }
        public Task<ResolveDiagnosticResult> Resolve(CancellationToken token = default, int? threshold = null,
            Func<Action, bool>? enqueue = null) => Track(Service.ReResolveWithDetailedDiagnosticsAsync(
                Items, enqueue ?? Ui.Enqueue, token, threshold, false));
        public async ValueTask DisposeAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _lifetime.Cancel();
            Service.CancelPendingResolution();
            foreach (var release in _release) release();
            // Observe expected test failures, but a cleanup deadline failure must fail the test.
            var observed = _operations.Select(async task => { try { await task; } catch { } });
            await Task.WhenAll(observed).WaitAsync(deadline.Token);
            Items.Clear();
            // A caller can finish before an uncancellable shared computation. A new empty
            // round can publish only after that computation exits; use it as a drain barrier.
            LoadConfig = () => Task.FromResult(Config);
            var barrier = Service.ReResolveWithDetailedDiagnosticsAsync(Items, Ui.Enqueue, default);
            await Ui.DrainUntil(barrier, deadline.Token);
            Service.CancelPendingResolution();
            foreach (var linked in _linked) linked.Dispose();
            _lifetime.Dispose();
            // Delete only after draining, and only the directory created by this fixture.
            Directory.Delete(Root, true);
        }
    }

    private static ArtworkInfo Info(ArtworkId id) => new(id, "Author", "42", "Title",
        null, ContentRating.R18, [new("tag", null)], DateTimeOffset.UtcNow, false);

    [Fact]
    public async Task AnalysisUsesRealClassifierAndPublishesBeforeCompletion()
    {
        await using var f = new Fixture();
        var source = f.Card("card");
        var task = f.Analyze(source, f.Card("plain", false));
        var add = await f.Ui.Next();
        Assert.Empty(f.Items);
        Assert.False(task.IsCompleted);
        add();
        await f.Ui.Run(); // Metadata.
        await f.Ui.Run(); // Readiness.
        await f.Ui.Run(); // Resolution snapshot.
        var publishDestination = await f.Ui.Next();
        Assert.False(task.IsCompleted);
        Assert.Null(Assert.Single(f.Items).DestinationPath);
        publishDestination();
        await f.Ui.DrainUntil(task);
        Assert.Equal(1, await task);
        var item = Assert.Single(f.Items);
        Assert.Equal(CardType.Character, item.CardType);
        Assert.Equal(ImportItemStatus.ReadyToImport, item.Status);
        Assert.Equal("42", item.AuthorId);
        Assert.NotNull(item.FetchedArtworkInfo);
        Assert.Equal(Path.Combine(f.Library, "Organized", "test", "Koikatsu", "R-18", "Author (42)", "card.png"), item.DestinationPath);
        Assert.True(File.Exists(source)); // Analysis never moves input.
    }

    [Fact]
    public async Task TwoBatchesCanPublishInReverseProviderOrder()
    {
        await using var f = new Fixture();
        var firstReply = f.Gate<ArtworkInfo?>();
        var requested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Provider.Fetch = (id, _) =>
        {
            if (id.Id != "first") return Task.FromResult<ArtworkInfo?>(Info(id));
            requested.TrySetResult(true);
            return firstReply.Task;
        };
        var first = f.Analyze(f.Card("first"));
        await f.Ui.Run();
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var second = f.Analyze(f.Card("second"));
        await f.Ui.DrainUntil(second);
        Assert.False(first.IsCompleted);
        firstReply.SetResult(Info(new("test", "first")));
        await f.Ui.DrainUntil(first);
        Assert.Equal(2, f.Items.Count);
        Assert.All(f.Items, item =>
        {
            Assert.Equal(ImportItemStatus.ReadyToImport, item.Status);
            Assert.Equal(Path.GetFileNameWithoutExtension(item.FileName), item.FetchedArtworkInfo!.ArtworkId.Id);
            Assert.Equal(Path.Combine(f.Library, "Organized", "test", "Koikatsu", "R-18",
                "Author (42)", item.FileName), item.DestinationPath);
        });
    }

    [Theory]
    [InlineData(Publication.Add)]
    [InlineData(Publication.Metadata)]
    [InlineData(Publication.Destination)]
    public async Task ClearAtQueuedPublicationDoesNotReviveOldBatch(Publication publication)
    {
        await using var f = new Fixture();
        using var cancel = new CancellationTokenSource();
        var task = f.Analyze([f.Card("old")], cancel.Token);
        var oldCallback = await f.Ui.HoldAnalysisAt(publication);
        cancel.Cancel();
        f.Service.CancelPendingResolution();
        f.Items.Clear();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(15)));
        var next = f.Analyze(f.Card("new"));
        await f.Ui.DrainUntil(next);
        var current = Assert.Single(f.Items);
        var destination = current.DestinationPath;
        oldCallback(); // Execute the stale publication after the new batch has completed.
        Assert.Same(current, Assert.Single(f.Items));
        Assert.Equal("new.png", current.FileName);
        Assert.Equal("new", current.FetchedArtworkInfo!.ArtworkId.Id);
        Assert.Equal(destination, current.DestinationPath);
    }

    [Fact]
    public async Task ProviderFailureAndDispatcherRejectionAreBounded()
    {
        await using var f = new Fixture();
        f.Provider.Fetch = (_, _) => throw new IOException("offline provider failure");
        var task = f.Analyze(f.Card("failed"));
        await f.Ui.DrainUntil(task);
        Assert.Equal(ImportItemStatus.ReadyToImport, Assert.Single(f.Items).Status);
        Assert.Null(f.Items[0].FetchedArtworkInfo);
        Assert.Contains("Import.FetchArtworkMetadata", f.Logger.Errors);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Analyze([f.Card("rejected")], default, _ => false).WaitAsync(TimeSpan.FromSeconds(15)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Resolve(enqueue: _ => false).WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DuplicateDetectionComparesContentNotJustName(bool same)
    {
        await using var f = new Fixture();
        var source = f.Card("duplicate");
        var existing = Path.Combine(f.Library, "duplicate.png");
        File.Copy(source, existing);
        if (!same)
        {
            var bytes = await File.ReadAllBytesAsync(existing);
            bytes[^1] ^= 1; // Same filename and length, different contents.
            await File.WriteAllBytesAsync(existing, bytes);
        }
        var task = f.Analyze(source);
        await f.Ui.DrainUntil(task);
        Assert.Equal(same ? ImportItemStatus.AlreadyInLibrary : ImportItemStatus.ReadyToImport,
            Assert.Single(f.Items).Status);
        Assert.True(File.Exists(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LatestOptionsWinAndCancelingOneWaitDoesNotCancelAnother(bool cancelFirst)
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SettingsService.ConfigData>(TaskCreationOptions.RunContinuationsAsynchronously);
        SettingsService.ConfigData config = null!;
        var calls = 0;
        await using var f = new Fixture(() =>
        {
            if (Interlocked.Increment(ref calls) == 1) { started.SetResult(true); return release.Task; }
            return Task.FromResult(config);
        });
        config = f.Config;
        f.ReleaseOnCleanup(() => release.TrySetCanceled());
        var item = new ImportItem
        {
            SourceFilePath = f.Card("one"), CardType = CardType.Character,
            GameVersion = GameVersion.Koikatsu, ArtworkId = new("test", "one"),
            AuthorId = "42", AuthorName = "Author", Title = "Title",
            Rating = ContentRating.R18, Status = ImportItemStatus.ReadyToImport
        };
        f.Items.Add(item);
        using var caller = new CancellationTokenSource();
        var first = f.Resolve(caller.Token, 99);
        await f.Ui.Run(); // Capture first pass.
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        // The existing policy creates a folder only when the count exceeds the threshold.
        var second = f.Resolve(threshold: 0);
        if (cancelFirst) caller.Cancel();
        release.SetResult(config);
        await f.Ui.DrainUntil(second);
        if (cancelFirst)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        else
            Assert.Same(await second, await first.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(Path.Combine(f.Library, "Organized", "test", "Koikatsu", "R-18",
            "Author (42)", "Title (one)", "one.png"), item.DestinationPath);
    }

    [Theory]
    [InlineData(Publication.Metadata)]
    [InlineData(Publication.Ready)]
    [InlineData(Publication.Snapshot)]
    [InlineData(Publication.Destination)]
    public async Task RejectedPublicationFailsWithoutApplyingAndNextBatchSucceeds(Publication publication)
    {
        await using var f = new Fixture();
        f.Provider.Parse = name => name == "unknown.png" ? null : new("test", Path.GetFileNameWithoutExtension(name));
        var task = f.Analyze([f.Card("known"), f.Card("unknown")], default, f.Ui.RejectAnalysisAt(publication));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Ui.DrainUntil(task));
        var known = f.Items.Single(item => item.FileName == "known.png");
        var unknown = f.Items.Single(item => item.FileName == "unknown.png");
        Assert.All(f.Items, item => Assert.Null(item.DestinationPath));
        Assert.Equal(publication == Publication.Metadata ? ImportItemStatus.Analyzing : ImportItemStatus.ReadyToImport, known.Status);
        Assert.Equal(publication <= Publication.Ready ? ImportItemStatus.Analyzing : ImportItemStatus.ReadyToImport, unknown.Status);
        if (publication == Publication.Metadata) Assert.Null(known.FetchedArtworkInfo);
        else Assert.Equal("known", known.FetchedArtworkInfo!.ArtworkId.Id);

        var retry = f.Analyze(f.Card("retry"));
        await f.Ui.DrainUntil(retry);
        var recovered = f.Items.Single(item => item.FileName == "retry.png");
        Assert.Equal(ImportItemStatus.ReadyToImport, recovered.Status);
        Assert.NotNull(recovered.DestinationPath);
    }

    [Fact]
    public async Task SettingsFailureReachesAllWaitersAndResolutionCanRetry()
    {
        await using var f = new Fixture();
        await f.Ui.DrainUntil(f.Analyze(f.Card("one")));
        var item = Assert.Single(f.Items);
        var original = item.DestinationPath;
        var reply = f.Gate<SettingsService.ConfigData>();
        var started = f.Gate<bool>();
        f.LoadConfig = () => { started.TrySetResult(true); return reply.Task; };
        var first = f.Resolve(threshold: 0);
        await f.Ui.Run();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var second = f.Resolve(threshold: 0);
        var failure = new IOException("settings unavailable");
        reply.SetException(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => f.Ui.DrainUntil(Task.WhenAll(first, second))));
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => first.WaitAsync(TimeSpan.FromSeconds(15))));
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => second.WaitAsync(TimeSpan.FromSeconds(15))));
        Assert.Equal(original, item.DestinationPath);
        f.LoadConfig = () => Task.FromResult(f.Config);
        await f.Ui.DrainUntil(f.Resolve(threshold: 0));
        Assert.Equal(Path.Combine(item.AuthorDirectoryPath!, "Title (one)", "one.png"), item.DestinationPath);
    }

    [Fact]
    public async Task SupersededSettingsFailureDoesNotFailLatestWaiters()
    {
        await using var f = new Fixture();
        await f.Ui.DrainUntil(f.Analyze(f.Card("one")));
        var reply = f.Gate<SettingsService.ConfigData>();
        var started = f.Gate<bool>();
        f.LoadConfig = () => { started.TrySetResult(true); return reply.Task; };
        var first = f.Resolve(threshold: 99);
        await f.Ui.Run();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        f.LoadConfig = () => Task.FromResult(f.Config);
        var second = f.Resolve(threshold: 0);
        reply.SetException(new IOException("stale settings failure"));
        await f.Ui.DrainUntil(Task.WhenAll(first, second));
        Assert.Same(await first, await second);
        var item = Assert.Single(f.Items);
        Assert.Equal(Path.Combine(item.AuthorDirectoryPath!, "Title (one)", "one.png"), item.DestinationPath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelPendingProviderDoesNotAffectAnotherBatch(bool clear)
    {
        await using var f = new Fixture();
        var reply = f.Gate<ArtworkInfo?>();
        var started = f.Gate<bool>();
        f.Provider.Fetch = (id, _) =>
        {
            if (id.Id != "old") return Task.FromResult<ArtworkInfo?>(Info(id));
            started.TrySetResult(true);
            return reply.Task; // Intentionally ignores cancellation.
        };
        using var cancel = new CancellationTokenSource();
        var first = f.Analyze([f.Card("old")], cancel.Token);
        await f.Ui.Run();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var old = Assert.Single(f.Items);
        // The other batch is already active when canceling only one batch.
        Task<int>? second = clear ? null : f.Analyze(f.Card("new"));
        cancel.Cancel();
        if (clear)
        {
            f.Service.CancelPendingResolution();
            f.Items.Clear();
            second = f.Analyze(f.Card("new"));
        }
        await f.Ui.DrainUntil(second!);
        var current = f.Items.Single(item => item.FileName == "new.png");
        var destination = current.DestinationPath;
        Assert.NotNull(destination);
        reply.SetResult(Info(new("test", "old")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Ui.DrainUntil(first));
        Assert.Null(old.FetchedArtworkInfo);
        Assert.Null(old.DestinationPath);
        Assert.Equal(ImportItemStatus.Analyzing, old.Status);
        Assert.Equal(clear ? 1 : 2, f.Items.Count);
        Assert.Equal("new", current.FetchedArtworkInfo!.ArtworkId.Id);
        Assert.Equal(destination, current.DestinationPath);
        Assert.Equal(ImportItemStatus.ReadyToImport, current.Status);
    }

    [Theory]
    [InlineData(Publication.Metadata)]
    [InlineData(Publication.Destination)]
    public async Task RemovedItemIsSkippedWithoutClearingOtherItems(Publication publication)
    {
        await using var f = new Fixture();
        f.Provider.Parse = _ => new("test", "shared");
        var task = f.Analyze(f.Card("removed"), f.Card("kept"));
        var publish = await f.Ui.HoldAnalysisAt(publication);
        var removed = f.Items.Single(item => item.FileName == "removed.png");
        var metadata = removed.FetchedArtworkInfo;
        var status = removed.Status;
        var changes = new List<string?>();
        removed.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        f.Items.Remove(removed);
        publish();
        await f.Ui.DrainUntil(task);
        Assert.Empty(changes);
        Assert.Same(metadata, removed.FetchedArtworkInfo);
        Assert.Equal(status, removed.Status);
        Assert.Null(removed.DestinationPath);
        var kept = Assert.Single(f.Items);
        Assert.Equal("kept.png", kept.FileName);
        Assert.Equal("shared", kept.FetchedArtworkInfo!.ArtworkId.Id);
        Assert.Equal(ImportItemStatus.ReadyToImport, kept.Status);
        Assert.NotNull(kept.DestinationPath);
    }

    [Fact]
    public async Task ExistingAuthorAndArtworkFolderNamesArePreserved()
    {
        await using var f = new Fixture();
        var artworkDirectory = Path.Combine(f.Library, "Organized", "test", "Koikatsu",
            "R-18", "Existing author (42)", "Existing title (one)");
        Directory.CreateDirectory(artworkDirectory);
        var task = f.Analyze(f.Card("one"));
        await f.Ui.DrainUntil(task);
        Assert.Equal(Path.Combine(artworkDirectory, "one.png"), Assert.Single(f.Items).DestinationPath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImportArtworkLookupSortsAndExcludesMetadataDirectory(bool hasArtworkFolders)
    {
        await using var f = new Fixture();
        var author = Path.Combine(f.Library, "Organized", "test", "Koikatsu", "R-18", "Author (42)");
        Directory.CreateDirectory(Path.Combine(author, PostMetadataStore.MetadataDirectoryName.ToUpperInvariant()));
        if (hasArtworkFolders)
            foreach (var name in new[] { "z-work", "A-work" })
                Directory.CreateDirectory(Path.Combine(author, name));
        var parsedNames = new List<string>();
        f.Provider.ParseArtworkFolder = name =>
        {
            parsedNames.Add(name);
            return new("TEST", "ONE"); // Even metadata would match if it reached the parser.
        };
        await f.Ui.DrainUntil(f.Analyze(f.Card("one")));
        Assert.Equal(Path.Combine(hasArtworkFolders ? Path.Combine(author, "A-work") : author, "one.png"),
            Assert.Single(f.Items).DestinationPath);
        Assert.Equal(hasArtworkFolders ? new[] { "A-work" } : Array.Empty<string>(), parsedNames);
    }

    [Fact]
    public async Task AuthorPostLookupPreservesEnumerationOrderAndDoesNotExcludeMetadata()
    {
        await using var f = new Fixture();
        var author = Path.Combine(f.Library, "author");
        Directory.CreateDirectory(Path.Combine(author, "z-work"));
        Directory.CreateDirectory(Path.Combine(author, "A-work"));
        var expected = Directory.EnumerateDirectories(author).First();
        f.Provider.ParseArtworkFolder = _ => new("TEST", "ONE");
        Assert.Equal(expected, AuthorPostService.FindArtworkDirectory(author, f.Provider, new("test", "one")));

        var metadata = Directory.CreateDirectory(Path.Combine(author, PostMetadataStore.MetadataDirectoryName)).FullName;
        f.Provider.ParseArtworkFolder = name => name == PostMetadataStore.MetadataDirectoryName ? new("test", "one") : null;
        Assert.Equal(metadata, AuthorPostService.FindArtworkDirectory(author, f.Provider, new("test", "one")));
    }

    [Fact]
    public async Task AuthorPostLookupReturnsNullOrPropagatesMissingDirectory()
    {
        await using var f = new Fixture();
        Directory.CreateDirectory(Path.Combine(f.Library, "Other (different)"));
        Assert.Null(AuthorPostService.FindArtworkDirectory(f.Library, f.Provider, new("test", "one")));
        Assert.Throws<DirectoryNotFoundException>(() => AuthorPostService.FindArtworkDirectory(
            Path.Combine(f.Library, "missing"), f.Provider, new("test", "one")));
    }
}

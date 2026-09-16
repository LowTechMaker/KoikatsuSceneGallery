using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.IntegrationTests;

/// <summary>
/// The staged-then-assigned flow, against a real file system. No XAML host, so
/// the view model is built with the synchronous publish shim and a pass-through
/// string resolver.
/// </summary>
public sealed class LocalImportViewModelTests
{
    private sealed class Logger : IAppLogger
    {
        public List<string> Errors { get; } = [];

        public void LogError(string operation, Exception exception, string? path = null)
            => Errors.Add(operation);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "ksg-local-vm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Library);

            Config = new SettingsService.ConfigData
            {
                CharacterFolderPaths = [Library],
                ArtworkSubfolderThreshold = 99,
            };

            Registry = new LocalSourceRegistry(Logger);
            Registry.UpdateConfiguration([Library], Config.ImportSubfolder, Config.LocalFolderName);

            var provider = new LocalSourceProvider(Registry);
            var store = new PostMetadataStore();
            var importService = new ImportService(
                [provider], [provider], null, () => Task.FromResult(Config),
                Logger, store, new LibraryFileCache());

            Coordinator = new ImportExecutionCoordinator(
                new ImportTransactionExecutor(store).ExecuteTransactionAsync,
                action => action());

            AuthorInfo = new AuthorInfoService([provider], null!, Logger);

            ViewModel = new LocalImportViewModel(
                importService,
                Coordinator,
                Registry,
                AuthorInfo,
                new ThumbnailCacheService(Logger, Path.Combine(Root, "thumbs")),
                Publish,
                dispatcher: null,
                getString: key => key,
                Logger);
        }

        public string Root { get; }
        public string Library => Path.Combine(Root, "library");
        public SettingsService.ConfigData Config { get; }
        public LocalSourceRegistry Registry { get; }
        public ImportExecutionCoordinator Coordinator { get; }
        public AuthorInfoService AuthorInfo { get; }
        public Logger Logger { get; } = new();
        public LocalImportViewModel ViewModel { get; }

        public static bool Publish(Action action)
        {
            action();
            return true;
        }

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

        /// <summary>
        /// A card in its own folder, so that two staged files can share a name,
        /// with <paramref name="filler"/> making their contents differ.
        /// </summary>
        public string CardIn(string folder, string name, int filler)
        {
            var directory = Path.Combine(Root, folder);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name + ".png");
            using var stream = File.Create(path);
            stream.Write(Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
            using var writer = new BinaryWriter(stream);
            writer.Write(100);
            writer.Write("KoiKatuChara");
            writer.Write(filler);
            return path;
        }

        public string PlainPng(string name)
        {
            var path = Path.Combine(Root, name + ".png");
            File.WriteAllBytes(path, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
            return path;
        }

        public void Dispose()
        {
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

    // The pending count has to be honest before the user commits, which is why
    // staging classifies rather than waiting for analysis.
    [Fact]
    public async Task StagingCountsOnlyCards()
    {
        using var f = new Fixture();

        await f.ViewModel.StageAsync([f.Card("a"), f.Card("b"), f.PlainPng("not-a-card")]);

        Assert.Equal(2, f.ViewModel.PendingCount);
        Assert.Equal(1, f.ViewModel.RejectedCount);
        Assert.True(f.ViewModel.HasStagedCards);
        Assert.True(f.ViewModel.CanAssign);
    }

    [Fact]
    public async Task StagingIgnoresDuplicatePathsAndNonPngFiles()
    {
        using var f = new Fixture();
        var card = f.Card("a");
        var text = Path.Combine(f.Root, "notes.txt");
        await File.WriteAllTextAsync(text, "x");

        await f.ViewModel.StageAsync([card, card.ToUpperInvariant(), text]);
        await f.ViewModel.StageAsync([card]);

        Assert.Equal(1, f.ViewModel.PendingCount);
    }

    [Fact]
    public async Task TheThumbnailStripIsCappedAndReportsTheRemainder()
    {
        using var f = new Fixture();
        var cards = Enumerable.Range(0, LocalImportViewModel.MaxStripThumbnails + 5)
            .Select(i => f.Card($"card{i}"))
            .ToArray();

        await f.ViewModel.StageAsync(cards);

        Assert.Equal(cards.Length, f.ViewModel.PendingCount);
        Assert.Equal(LocalImportViewModel.MaxStripThumbnails, f.ViewModel.VisibleStagedCards.Count);
        Assert.Equal(5, f.ViewModel.HiddenThumbnailCount);
        Assert.True(f.ViewModel.HasHiddenThumbnails);
    }

    [Fact]
    public async Task ClearStagingEmptiesTheBatchAndIssuesANewToken()
    {
        using var f = new Fixture();
        await f.ViewModel.StageAsync([f.Card("a"), f.PlainPng("plain")]);
        var token = f.ViewModel.StagedBatchToken;

        f.ViewModel.ClearStaging();

        Assert.Equal(0, f.ViewModel.PendingCount);
        Assert.Equal(0, f.ViewModel.RejectedCount);
        Assert.Empty(f.ViewModel.VisibleStagedCards);
        Assert.False(f.ViewModel.CanAssign);
        Assert.NotEqual(token, f.ViewModel.StagedBatchToken);
    }

    [Fact]
    public async Task RevalidateStagingDropsFilesThatVanished()
    {
        using var f = new Fixture();
        var gone = f.Card("gone");
        await f.ViewModel.StageAsync([f.Card("kept"), gone]);

        File.Delete(gone);
        f.ViewModel.RevalidateStaging();

        Assert.Equal(1, f.ViewModel.PendingCount);
        Assert.Equal("kept.png", Assert.Single(f.ViewModel.VisibleStagedCards).FileName);
    }

    [Fact]
    public async Task AssigningImportsIntoTheSourceFolderAndRecordsItsDescription()
    {
        using var f = new Fixture();
        var source = f.ViewModel.CreatePendingSource("阿明");
        await f.ViewModel.StageAsync([f.Card("card")]);
        var committed = new List<string>();
        f.ViewModel.ImportCommitted += paths => committed.AddRange(paths);

        await f.ViewModel.ImportStagedToAsync(source);

        var landed = Assert.Single(committed);
        var directory = Path.GetDirectoryName(landed)!;
        Assert.Equal($"阿明 ({source.Id})", Path.GetFileName(directory));
        Assert.True(File.Exists(landed));
        Assert.Equal("阿明", new LocalSourceStore().Read(directory)?.DisplayName);

        // The batch is consumed by a successful import.
        Assert.Equal(0, f.ViewModel.PendingCount);
        Assert.False(f.ViewModel.CanAssign);
    }

    // A source created in the UI has no folder yet; the transaction creates it.
    [Fact]
    public async Task APendingSourceBecomesRealAfterItsFirstImport()
    {
        using var f = new Fixture();
        var source = f.ViewModel.CreatePendingSource("阿明");
        Assert.Null(source.Directory);
        await f.ViewModel.StageAsync([f.Card("card")]);

        await f.ViewModel.ImportStagedToAsync(source);
        await f.Registry.RescanAsync();

        var resolved = f.Registry.Find(source.Id);
        Assert.NotNull(resolved);
        Assert.NotNull(resolved.Directory);
        Assert.True(Directory.Exists(resolved.Directory));
        Assert.Equal("阿明", resolved.DisplayName);
    }

    // The most likely real-world dead end: a card dragged in from a folder the
    // library already holds a copy of. It must say so, not fail silently.
    [Fact]
    public async Task AssigningAFileAlreadyInTheLibraryReportsItAndKeepsTheBatch()
    {
        using var f = new Fixture();
        var card = f.Card("dup");
        File.Copy(card, Path.Combine(f.Library, "dup.png"));
        var source = f.ViewModel.CreatePendingSource("阿明");
        await f.ViewModel.StageAsync([card]);

        var reported = new List<string>();
        f.ViewModel.DuplicatesKept += paths => reported.AddRange(paths);

        await f.ViewModel.ImportStagedToAsync(source);

        Assert.Equal("LocalSources_ImportAlreadyInLibrary", f.ViewModel.StatusText);
        Assert.Equal(1, f.ViewModel.PendingCount);
        Assert.True(f.ViewModel.CanAssign);
        Assert.Contains("LocalImport.NothingEligible", f.Logger.Errors);

        // Named, so the dialog can point at the file that is still sitting
        // where the user dropped it from.
        Assert.Equal(card, Assert.Single(reported));
        Assert.True(File.Exists(card));
    }

    // The silent case behind the report: in a mixed batch the cards the
    // library already has were dropped from the plan and vanished from the
    // strip without a word.
    [Fact]
    public async Task AMixedBatchImportsTheRestAndNamesTheCardsItLeftBehind()
    {
        using var f = new Fixture();
        var duplicate = f.CardIn("drop", "dup", filler: 1);
        var fresh = f.CardIn("drop", "fresh", filler: 2);
        File.Copy(duplicate, Path.Combine(f.Library, "dup.png"));
        var source = f.ViewModel.CreatePendingSource("阿明");
        var committed = new List<string>();
        var reported = new List<string>();
        f.ViewModel.ImportCommitted += paths => committed.AddRange(paths);
        f.ViewModel.DuplicatesKept += paths => reported.AddRange(paths);
        await f.ViewModel.StageAsync([duplicate, fresh]);

        await f.ViewModel.ImportStagedToAsync(source);

        Assert.Equal("fresh.png", Path.GetFileName(Assert.Single(committed)));
        Assert.Equal(duplicate, Assert.Single(reported));

        // Left exactly where it was: not moved, not deleted.
        Assert.True(File.Exists(duplicate));
        Assert.False(File.Exists(fresh));
        Assert.Equal(
            "LocalSources_ImportSucceededWithDuplicates",
            f.ViewModel.StatusText);
    }

    [Fact]
    public async Task ABatchWithNoDuplicatesReportsNone()
    {
        using var f = new Fixture();
        var source = f.ViewModel.CreatePendingSource("阿明");
        var reported = new List<string>();
        f.ViewModel.DuplicatesKept += paths => reported.AddRange(paths);
        await f.ViewModel.StageAsync([f.Card("card")]);

        await f.ViewModel.ImportStagedToAsync(source);

        Assert.Empty(reported);
        Assert.Equal("LocalSources_ImportSucceeded", f.ViewModel.StatusText);
    }

    // The reported failure: one card whose name the destination already holds
    // with different content took the whole batch down, and the message named
    // neither the card nor the right number.
    [Fact]
    public async Task ADifferentCardWithATakenNameLandsBesideItAndKeepsTheBatch()
    {
        using var f = new Fixture();
        var source = f.ViewModel.CreatePendingSource("阿明");
        await f.ViewModel.StageAsync([f.CardIn("first", "card", filler: 1)]);
        await f.ViewModel.ImportStagedToAsync(source);
        await f.Registry.RescanAsync();
        var resolved = f.Registry.Find(source.Id)!;

        var committed = new List<string>();
        f.ViewModel.ImportCommitted += paths => committed.AddRange(paths);
        await f.ViewModel.StageAsync(
        [
            f.CardIn("second", "card", filler: 2),
            f.CardIn("second", "other", filler: 2),
        ]);

        await f.ViewModel.ImportStagedToAsync(resolved);

        Assert.Equal(2, committed.Count);
        Assert.Contains(committed, path => Path.GetFileName(path) == "card_1.png");
        Assert.Contains(committed, path => Path.GetFileName(path) == "other.png");
        Assert.Equal(0, f.ViewModel.PendingCount);
        Assert.DoesNotContain("LocalImport.TransactionFailed", f.Logger.Errors);
    }

    // Two unrelated cards sharing a name inside one batch collide with each
    // other rather than with the library, which no destination check catches.
    [Fact]
    public async Task TwoStagedCardsSharingANameBothLand()
    {
        using var f = new Fixture();
        var source = f.ViewModel.CreatePendingSource("阿明");
        var committed = new List<string>();
        f.ViewModel.ImportCommitted += paths => committed.AddRange(paths);
        await f.ViewModel.StageAsync(
        [
            f.CardIn("first", "card", filler: 1),
            f.CardIn("second", "card", filler: 2),
        ]);

        await f.ViewModel.ImportStagedToAsync(source);

        Assert.Equal(2, committed.Count);
        Assert.Equal(
            ["card.png", "card_1.png"],
            committed.Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    // Same name, same content: still folded away rather than imported twice.
    [Fact]
    public async Task TwoIdenticalStagedCardsStillProduceOneFile()
    {
        using var f = new Fixture();
        var source = f.ViewModel.CreatePendingSource("阿明");
        var committed = new List<string>();
        f.ViewModel.ImportCommitted += paths => committed.AddRange(paths);
        await f.ViewModel.StageAsync(
        [
            f.CardIn("first", "card", filler: 1),
            f.CardIn("second", "card", filler: 1),
        ]);

        await f.ViewModel.ImportStagedToAsync(source);

        var landed = Assert.Single(committed);
        Assert.Equal("card.png", Path.GetFileName(landed));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(landed)!, "*.png"));
    }

    [Fact]
    public async Task AssigningWithNoLibraryFolderConfiguredSaysSo()
    {
        using var f = new Fixture();
        f.Config.CharacterFolderPaths = [];
        var source = f.ViewModel.CreatePendingSource("阿明");
        await f.ViewModel.StageAsync([f.Card("card")]);

        await f.ViewModel.ImportStagedToAsync(source);

        Assert.Equal("LocalSources_ImportNoDestination", f.ViewModel.StatusText);
        Assert.Equal(1, f.ViewModel.PendingCount);
    }

    [Fact]
    public async Task AssigningWithNothingStagedDoesNothing()
    {
        using var f = new Fixture();
        var source = f.ViewModel.CreatePendingSource("阿明");

        await f.ViewModel.ImportStagedToAsync(source);

        Assert.Empty(f.Logger.Errors);
        Assert.Equal("", f.ViewModel.StatusText);
    }

    // The execution coordinator allows one transaction at a time and is shared
    // with the online import page, so a busy one must surface, not throw.
    [Fact]
    public async Task AssigningWhileATransactionRunsReportsBusy()
    {
        using var f = new Fixture();
        var source = f.ViewModel.CreatePendingSource("阿明");
        await f.ViewModel.StageAsync([f.Card("card")]);

        var blocker = new TaskCompletionSource();
        var busyCoordinator = new ImportExecutionCoordinator(
            (_, _, _) => blocker.Task.ContinueWith(_ => (ImportExecutionReceipt)null!),
            action => action());
        var occupied = busyCoordinator.ExecuteAsync([], _ => { });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => busyCoordinator.ExecuteAsync([], _ => { }));

        blocker.SetResult();
        try
        {
            await occupied;
        }
        catch
        {
            // The stub receipt is null; only the gate matters here.
        }
    }

    // AuthorInfoService raises its change event once per card as a gallery
    // loads, so a rebuild that runs when nothing differs tears down every tile
    // container — and each one restarts its own animation.
    [Fact]
    public async Task ANotificationThatChangesNothingLeavesTheTilesAlone()
    {
        using var f = new Fixture();
        var source = f.ViewModel.CreatePendingSource("阿明");
        await f.ViewModel.StageAsync([f.Card("card")]);
        await f.ViewModel.ImportStagedToAsync(source);
        var directory = f.Registry.Find(source.Id)?.Directory;
        Assert.NotNull(directory);

        var changes = 0;
        f.ViewModel.Tiles.CollectionChanged += (_, _) => changes++;

        // Same identity, same name: nothing a tile shows has changed.
        await f.Registry.EnsureDocumentAsync(directory, source.Id, "阿明");
        await f.Registry.EnsureDocumentAsync(directory, source.Id, "阿明");

        Assert.Equal(0, changes);

        // A real change still comes through.
        await f.Registry.EnsureDocumentAsync(directory, source.Id, "小明");
        Assert.True(changes > 0);
        Assert.Equal("小明", ((LocalSourceTile)f.ViewModel.Tiles[0]).Summary.Display.Name);
    }

    [Fact]
    public void TilesAlwaysEndWithTheAddCell()
    {
        using var f = new Fixture();

        Assert.Same(AddLocalSourceTile.Instance, f.ViewModel.Tiles[^1]);

        f.ViewModel.CreatePendingSource("阿明");

        Assert.Equal(2, f.ViewModel.Tiles.Count);
        Assert.IsType<LocalSourceTile>(f.ViewModel.Tiles[0]);
        Assert.Same(AddLocalSourceTile.Instance, f.ViewModel.Tiles[^1]);
    }
}

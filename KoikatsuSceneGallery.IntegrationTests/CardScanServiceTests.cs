using System.Collections.Concurrent;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.IntegrationTests;

public sealed class CardScanServiceTests
{
    private sealed class Scanner : CardScanService<CoordinateCard>
    {
        public readonly ConcurrentQueue<string> Enumerated = new();
        public Action? BeforeParse;
        protected override IEnumerable<FileInfo> EnumerateCardFiles(string folder)
        {
            Enumerated.Enqueue(folder);
            return new DirectoryInfo(folder).EnumerateFiles();
        }
        protected override CoordinateCard? TryCreateCard(FileInfo info)
        {
            BeforeParse?.Invoke();
            return info.Extension == ".skip" ? null : new() { FilePath = info.FullName };
        }
        protected override void ConfigureWatcher(FileSystemWatcher watcher) { }
    }

    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ksg-scan-" + Guid.NewGuid().ToString("N"));
        public Files()
        {
            Directory.CreateDirectory(Root);
            for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(Root, i + ".card"), "fixture");
            File.WriteAllText(Path.Combine(Root, "rejected.skip"), "fixture");
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static async Task<List<CoordinateCard>> Scan(Scanner scanner, IEnumerable<string> roots,
        bool batched, CancellationToken token = default)
    {
        if (!batched) return await scanner.ScanFoldersAsync(roots, token).WaitAsync(TimeSpan.FromSeconds(15));
        var batches = new ConcurrentQueue<List<CoordinateCard>>();
        await scanner.ScanFoldersAsync(roots, batches.Enqueue, token, batchSize: 2).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.All(batches, batch => Assert.InRange(batch.Count, 1, 2));
        return batches.SelectMany(x => x).ToList();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothEntrypointsSkipMissingRootsAndRejectedCards(bool batched)
    {
        using var files = new Files();
        using var scanner = new Scanner();
        var cards = await Scan(scanner, [Path.Combine(files.Root, "missing"), files.Root], batched);
        Assert.Equal(5, cards.Count);
        Assert.Equal(5, cards.Select(x => x.FilePath).Distinct().Count());
        Assert.All(cards, card => Assert.EndsWith(".card", card.FilePath));
        Assert.Equal(files.Root, Assert.Single(scanner.Enumerated));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCancellationDoesNotEnumerate(bool batched)
    {
        using var files = new Files();
        using var scanner = new Scanner();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Scan(scanner, [files.Root], batched, new CancellationToken(true)));
        Assert.Empty(scanner.Enumerated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringParseStopsTheScanAndNextScanCanRun(bool batched)
    {
        using var files = new Files();
        using var scanner = new Scanner();
        using var source = new CancellationTokenSource();
        scanner.BeforeParse = source.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Scan(scanner, [files.Root], batched, source.Token));
        scanner.BeforeParse = null;
        Assert.Equal(5, (await Scan(scanner, [files.Root], batched)).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParserErrorsAreReportedAndNextScanCanRun(bool batched)
    {
        using var files = new Files();
        using var scanner = new Scanner();
        var failure = new IOException("parser failed");
        scanner.BeforeParse = () => throw failure;
        var error = await Assert.ThrowsAsync<AggregateException>(() => Scan(scanner, [files.Root], batched));
        Assert.All(error.Flatten().InnerExceptions, exception => Assert.Same(failure, exception));
        scanner.BeforeParse = null;
        Assert.Equal(5, (await Scan(scanner, [files.Root], batched)).Count);
    }

    [Fact]
    public async Task BatchCallbackFailureIsObservedByAwaiter()
    {
        using var files = new Files();
        using var scanner = new Scanner();
        var failure = new IOException("publication failed");
        var error = await Assert.ThrowsAsync<AggregateException>(() => scanner.ScanFoldersAsync(
            [files.Root], _ => throw failure, batchSize: 1).WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.All(error.Flatten().InnerExceptions, exception => Assert.Same(failure, exception));
        Assert.Equal(5, (await Scan(scanner, [files.Root], true)).Count);
    }
}

using System.Collections.ObjectModel;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery.XamlTestHost;

internal static class GalleryLoadChecks
{
    internal static async Task RunAsync()
    {
        RunThumbnailSessionChecks();

        using var scheduler = new ThumbnailPriorityScheduler(capacity: 1, workerCount: 1);
        var cards = new ObservableCollection<CoordinateCard>();
        var gallery = new TestGallery(cards, scheduler);
        var logger = new RecordingLogger();
        int reloads = 0;
        gallery.CardsReloaded += () => reloads++;
        try
        {
            bool metadataCanceled = false;
            var first = new CoordinateCard { FilePath = "first.png" };
            await gallery.Load(async token =>
            {
                Require(gallery.IsLoading && metadataCanceled, "load setup before callback");
                await Task.Run(() => gallery.Publish(() => cards.Add(first), token));
                Require(cards.Count == 1, "batch publication awaited");
            }, logger, () => metadataCanceled = true).WaitAsync(TimeSpan.FromSeconds(5));
            Require(!gallery.IsLoading && !gallery.IsEmpty && reloads == 1, "successful load completion");
            Require(gallery.CardsView.Count == 1 && ReferenceEquals(gallery.CardsView[0], first), "deferred view published");
            Console.WriteLine("PASS: gallery load setup and awaited batch");

            var oldRelease = Signal();
            var latestRelease = Signal();
            CancellationToken oldToken = default;
            var oldLoad = gallery.Load(async token =>
            {
                oldToken = token;
                await oldRelease.Task;
                token.ThrowIfCancellationRequested();
            }, logger);
            var latestLoad = gallery.Load(async _ => await latestRelease.Task, logger);
            try
            {
                Require(oldToken.IsCancellationRequested, "new load cancels previous token");
                oldRelease.SetResult();
                await oldLoad.WaitAsync(TimeSpan.FromSeconds(5));
                Require(gallery.IsLoading && reloads == 2, "old completion cannot clear new loading state");
                Require(logger.Errors.Count == 1 && logger.Errors[0] is OperationCanceledException,
                    "owned cancellation is recorded");
                bool staleApplied = false;
                try
                {
                    await Task.Run(() => gallery.Publish(() => staleApplied = true, oldToken))
                        .WaitAsync(TimeSpan.FromSeconds(5));
                    throw new InvalidOperationException("Canceled publication unexpectedly completed");
                }
                catch (OperationCanceledException) { }
                Require(!staleApplied, "canceled batch does not run");
                latestRelease.SetResult();
                await latestLoad.WaitAsync(TimeSpan.FromSeconds(5));
                Require(!gallery.IsLoading && reloads == 3, "latest completion clears loading");
            }
            finally
            {
                gallery.CancelPendingWork();
                oldRelease.TrySetResult();
                latestRelease.TrySetResult();
                await Task.WhenAll(oldLoad, latestLoad).WaitAsync(TimeSpan.FromSeconds(5));
            }
            Console.WriteLine("PASS: gallery overlapping load cancellation isolation");

            var expected = new InvalidOperationException("offline load failure");
            try
            {
                await gallery.Load(_ =>
                {
                    cards.Clear();
                    throw expected;
                }, logger);
                throw new InvalidOperationException("Load failure unexpectedly swallowed");
            }
            catch (InvalidOperationException error) when (ReferenceEquals(error, expected)) { }
            Require(!gallery.IsLoading && gallery.IsEmpty && gallery.CardsView.Count == 0 && reloads == 4,
                "failure releases deferral and loading state");

            var unrelatedCancellation = new OperationCanceledException("not the load token");
            try
            {
                await gallery.Load(_ => Task.FromException(unrelatedCancellation), logger);
                throw new InvalidOperationException("Unrelated cancellation unexpectedly swallowed");
            }
            catch (OperationCanceledException error) when (ReferenceEquals(error, unrelatedCancellation)) { }
            Require(!gallery.IsLoading && reloads == 5 && logger.Errors.Count == 1,
                "unrelated cancellation propagates without owned-cancellation logging");

            await gallery.Load(token => Task.Run(() => gallery.Publish(() => cards.Add(first), token)), logger)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Require(!gallery.IsLoading && gallery.CardsView.Count == 1 && reloads == 6, "retry after failure");
            Console.WriteLine("PASS: gallery load failure cleanup and retry");

            var index = new Dictionary<string, CoordinateCard>(StringComparer.OrdinalIgnoreCase)
            {
                [first.FilePath] = first
            };
            var second = new CoordinateCard { FilePath = "second.png" };
            var third = new CoordinateCard { FilePath = "third.png" };
            CoordinateCard[] batch = [new() { FilePath = "FIRST.PNG" }, second,
                new() { FilePath = "SECOND.PNG" }, third];
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            bool changedOffUi = false;
            System.Collections.Specialized.NotifyCollectionChangedEventHandler trackThread =
                (_, _) => changedOffUi |= !dispatcher.HasThreadAccess;
            cards.CollectionChanged += trackThread;
            try
            {
                await gallery.Load(token => Task.Run(() => gallery.PublishCards(batch, index, token)), logger)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                Require(!changedOffUi && cards.Count == 3 && index.Count == 3, "batch mutates source only on UI thread");
                Require(ReferenceEquals(cards[0], first) && ReferenceEquals(cards[1], second)
                    && ReferenceEquals(cards[2], third), "batch preserves first instances and order");
                Require(ReferenceEquals(index["FIRST.PNG"], first) && ReferenceEquals(index["SECOND.PNG"], second),
                    "batch keeps caller dictionary comparer and existing instances");
                await Task.Run(() => gallery.PublishCards(batch, index, default)).WaitAsync(TimeSpan.FromSeconds(5));
                Require(cards.Count == 3 && gallery.CardsView.Count == 3, "repeated batch does not add duplicates");
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                try
                {
                    await Task.Run(() => gallery.PublishCards([new() { FilePath = "canceled.png" }], index, canceled.Token))
                        .WaitAsync(TimeSpan.FromSeconds(5));
                    throw new InvalidOperationException("Canceled scan batch unexpectedly completed");
                }
                catch (OperationCanceledException) { }
                Require(cards.Count == 3 && index.Count == 3, "canceled batch leaves source and index unchanged");
            }
            finally { cards.CollectionChanged -= trackThread; }
            Console.WriteLine("PASS: gallery scanned cards preserve order duplicates and cancellation");

            var disposalRelease = Signal();
            CancellationToken disposingLoadToken = default;
            var disposingLoad = gallery.Load(async token =>
            {
                disposingLoadToken = token;
                await disposalRelease.Task;
                token.ThrowIfCancellationRequested();
            }, logger);
            try
            {
                var thumbnailToken = gallery.ReadThumbnailToken();
                var canceledOrder = new List<string>();
                using var loadRegistration = disposingLoadToken.Register(() => canceledOrder.Add("load"));
                using var thumbnailRegistration = thumbnailToken.Register(() => canceledOrder.Add("thumbnail"));
                gallery.DisposeSources();
                Require(disposingLoadToken.IsCancellationRequested && thumbnailToken.IsCancellationRequested,
                    "both work sources canceled before load finishes");
                Require(canceledOrder.SequenceEqual(new[] { "load", "thumbnail" }), "work cancellation order");
                RequireDisposed(gallery.ReadLoadToken);
                RequireDisposed(gallery.ReadThumbnailToken);
                disposalRelease.SetResult();
                await disposingLoad.WaitAsync(TimeSpan.FromSeconds(5));
                Require(!gallery.IsLoading && logger.Errors.Count == 2, "disposed source allows canceled load to finish");
            }
            finally
            {
                disposalRelease.TrySetResult();
                await disposingLoad.WaitAsync(TimeSpan.FromSeconds(5));
            }
            Console.WriteLine("PASS: gallery disposal cancels sources and permits load cleanup");
        }
        finally { gallery.Stop(); }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void RequireDisposed(Func<CancellationToken> readToken)
    {
        try { _ = readToken(); }
        catch (ObjectDisposedException) { return; }
        throw new InvalidOperationException("Work source was not disposed");
    }
    private static void Require(bool value, string operation)
    {
        if (!value) throw new InvalidOperationException("Gallery load check failed: " + operation);
    }

    private sealed class RecordingLogger : IAppLogger
    {
        internal List<Exception> Errors { get; } = [];
        public void LogError(string operation, Exception exception, string? path = null)
        {
            Require(operation == "Test.LoadCanceled", "cancellation operation name");
            Errors.Add(exception);
        }
    }

    // Reset and activation share one session restart on the production base.
    // A live session must be left alone, and a retired one must be replaced by
    // a source that is neither cancelled nor the disposed previous instance.
    private static void RunThumbnailSessionChecks()
    {
        using var scheduler = new ThumbnailPriorityScheduler(capacity: 1, workerCount: 1);
        var gallery = new TestGallery(new ObservableCollection<CoordinateCard>(), scheduler);
        try
        {
            // A freshly constructed gallery holds no source until the first
            // reset or activation, which is what a real load performs.
            gallery.Reset();
            var live = gallery.ReadThumbnailToken();
            gallery.ActivateThumbnailRequests();
            Require(gallery.ReadThumbnailToken() == live, "activation leaves a live session alone");

            gallery.CancelPendingWork();
            Require(live.IsCancellationRequested, "pending work cancels the thumbnail session");

            gallery.ActivateThumbnailRequests();
            var revived = gallery.ReadThumbnailToken();
            Require(!revived.IsCancellationRequested, "activation installs a live session");
            Require(revived != live, "activation does not reuse the retired source");

            gallery.Reset();
            var afterReset = gallery.ReadThumbnailToken();
            Require(!afterReset.IsCancellationRequested && afterReset != revived,
                "reset retires the session the same way");
            Console.WriteLine("PASS: gallery thumbnail session restart");
        }
        finally
        {
            gallery.Stop();
        }
    }

    private sealed class TestGallery : GalleryViewModelBase
    {
        private bool _sourcesDisposed;
        internal TestGallery(ObservableCollection<CoordinateCard> cards, ThumbnailPriorityScheduler scheduler)
            : base(cards, scheduler) => ApplyFilter();
        internal Task Load(Func<CancellationToken, Task> load, IAppLogger logger, Action? cancelMetadata = null)
            => RunLoadAsync(load, logger, "Test.LoadCanceled", cancelMetadata);
        internal void Publish(Action apply, CancellationToken token) => PublishScannedBatch(apply, token);
        internal void PublishCards(IEnumerable<CoordinateCard> cards, Dictionary<string, CoordinateCard> index,
            CancellationToken token) => PublishScannedCards(cards, index, token);
        internal void Stop()
        {
            if (_sourcesDisposed) return; // The disposal scenario already owns this cleanup.
            CancelPendingWork();
            _loadCts?.Dispose();
            _thumbnailCts?.Dispose();
        }
        internal CancellationToken ReadLoadToken() => _loadCts!.Token;
        internal CancellationToken ReadThumbnailToken() => _thumbnailCts!.Token;
        internal void Reset() => ResetThumbnailState();
        internal void DisposeSources()
        {
            DisposeWorkCancellationSources();
            _sourcesDisposed = true;
        }
        protected override bool CardPassesFilter(object card) => card is CoordinateCard;
        protected override void ApplyFilter()
        {
            CardsView.Filter = CardPassesFilter;
            RefreshFilterAndNotify();
        }
    }
}

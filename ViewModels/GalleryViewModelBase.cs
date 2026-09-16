using System.Collections;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI.Collections;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.ViewModels;

public enum SortOption
{
    Name,
    DateModified,
    FileSize,
    Shuffle
}

public static class ShuffleConstants
{
    public const int PoolSize = 20;
}

public abstract partial class GalleryViewModelBase : ObservableObject
{
    protected string[] _searchKeywords = [];
    protected bool _resolutionFilterEnabled;
    protected HashSet<string> _allowedResolutions = [];
    protected bool HasResolutionFilter => _resolutionFilterEnabled && _allowedResolutions.Count > 0;

    private int _shuffleDisplayCount;
    private readonly GalleryShuffleQueue _shuffle = new(ShuffleConstants.PoolSize);

    protected CancellationTokenSource? _thumbnailCts;
    protected CancellationTokenSource? _loadCts;
    protected readonly Dictionary<string, string> _thumbnailPathCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ThumbnailPriorityScheduler _thumbnailScheduler;
    private readonly Dictionary<string, ThumbnailRequest> _thumbnailRequests =
        new(StringComparer.OrdinalIgnoreCase);
    private long _thumbnailSession;

    protected readonly DispatcherQueue _dispatcherQueue;
    protected readonly IList _cardsSource;

    public AdvancedCollectionView CardsView { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShuffleMode))]
    public partial SortOption SelectedSort { get; set; } = SortOption.Name;

    [ObservableProperty]
    public partial bool SortAscending { get; set; } = true;

    public bool IsShuffleMode => SelectedSort == SortOption.Shuffle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    public bool IsEmpty => !IsLoading && CardsView.Count == 0;

    [ObservableProperty]
    public partial bool ShowFileNames { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneratingThumbnails))]
    public partial int PendingThumbnailCount { get; set; }

    public bool IsGeneratingThumbnails => PendingThumbnailCount > 0;

    public event Action? CardsReloaded;
    public event Action? ViewRefreshed;

    protected abstract bool CardPassesFilter(object card);
    protected abstract void ApplyFilter();

    protected TCard? GetRandomVisibleCard<TCard>() where TCard : CardBase
    {
        if (CardsView.Count == 0) return null;
        return CardsView[Random.Shared.Next(CardsView.Count)] as TCard;
    }

    protected void OnResolutionFilterChanged(bool enabled, HashSet<string> resolutions)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            _resolutionFilterEnabled = enabled;
            _allowedResolutions = resolutions;
            if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
            ApplyFilter();
        });
    }

    public virtual bool MatchesBrowseCard(CardBase card, IReadOnlyList<string> keywords)
        => GallerySearch.Matches(card.FilePath, (card as IAuthorOwner)?.Author?.Name, keywords);

    protected GalleryViewModelBase(IList cardsSource, ThumbnailPriorityScheduler thumbnailScheduler)
    {
        _cardsSource = cardsSource;
        _thumbnailScheduler = thumbnailScheduler;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        CardsView = new AdvancedCollectionView(cardsSource, true);
        if (cardsSource is INotifyCollectionChanged observable)
            observable.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        ApplySort();
    }

    /// <summary>
    /// Where a card came from, as opposed to what it contains. Shared by every
    /// gallery whose cards can carry an author, and session-only like the other
    /// browser filters.
    /// </summary>
    [ObservableProperty]
    public partial CardOriginFilter OriginFilter { get; set; }

    protected bool HasOriginFilter => OriginFilter != CardOriginFilter.All;

    protected bool OriginPasses(object? card)
        => !HasOriginFilter || CardOriginQuery.Passes(CardOrigin.ProviderIdOf(card), OriginFilter);

    partial void OnOriginFilterChanged(CardOriginFilter value)
    {
        if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchKeywords = value.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToArray();
        if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
        ApplyFilter();
    }

    partial void OnSelectedSortChanged(SortOption value)
    {
        if (value == SortOption.Shuffle)
            BuildShuffleQueue();
        else
            ClearShuffleState();
        ApplySort();
        ApplyFilter();
    }

    partial void OnSortAscendingChanged(bool value) => ApplySort();

    public void SetShuffleDisplayCount(int count)
    {
        if (_shuffleDisplayCount == count) return;
        _shuffleDisplayCount = count;
        if (IsShuffleMode) ApplyFilter();
    }

    public void Reshuffle()
    {
        AdvanceShuffleQueue();
        ApplySort();
        ApplyFilter();
    }

    protected void ApplySort()
    {
        using (CardsView.DeferRefresh())
        {
            CardsView.SortDescriptions.Clear();
            if (SelectedSort == SortOption.Shuffle)
            {
                CardsView.SortDescriptions.Add(
                    new SortDescription(SortDirection.Ascending, _shuffle.Comparer));
                return;
            }
            var direction = SortAscending ? SortDirection.Ascending : SortDirection.Descending;
            string propertyName = SelectedSort switch
            {
                SortOption.Name => nameof(CardBase.FileName),
                SortOption.DateModified => nameof(CardBase.DateModified),
                SortOption.FileSize => nameof(CardBase.FileSize),
                _ => nameof(CardBase.FileName)
            };
            CardsView.SortDescriptions.Add(new SortDescription(propertyName, direction));
        }
    }

    protected void BuildShuffleQueue() => _shuffle.Build(_cardsSource, CardPassesFilter);

    private void AdvanceShuffleQueue() =>
        _shuffle.Advance(_cardsSource, CardPassesFilter, _shuffleDisplayCount);

    private void ClearShuffleState() => _shuffle.Clear();

    protected bool TryApplyShuffleFilter()
    {
        if (!IsShuffleMode) return false;

        var displaySet = _shuffle.GetDisplaySet(_shuffleDisplayCount);

        CardsView.Filter = item => displaySet.Contains(item);
        RefreshFilterAndNotify();
        return true;
    }

    /// <summary>
    /// Requests a thumbnail using the existing per-card request lifecycle. Call on the UI thread.
    /// The generator receives the request-owned cancellation token. onGenerated runs only for
    /// a non-null generated path before UI dispatch (never for cache hits); its exceptions use
    /// the same logging and completion path as generation failures.
    /// </summary>
    protected void RequestThumbnailCore<TCard>(
        TCard card,
        ThumbnailWorkPriority priority,
        Func<TCard, string?> tryGetCachedPath,
        Func<TCard, CancellationToken, Task<string?>> generateAsync,
        IAppLogger logger,
        string logPrefix,
        Action<TCard, string>? onGenerated = null) where TCard : CardBase
    {
        if (card.HasThumbnail) return;
        if (_thumbnailPathCache.TryGetValue(card.FilePath, out var cached))
        {
            card.ThumbnailPath = cached;
            return;
        }

        var diskCached = tryGetCachedPath(card);
        if (diskCached is not null)
        {
            _thumbnailPathCache[card.FilePath] = diskCached;
            card.ThumbnailPath = diskCached;
            return;
        }

        if (!TryBeginThumbnailRequest(card.FilePath, priority, out var request)) return;
        _ = ScheduleThumbnailRequest(request, token => GenerateThumbnailAsync(
            card, request, token, generateAsync, logger, logPrefix, onGenerated));
    }

    private async Task GenerateThumbnailAsync<TCard>(
        TCard card,
        ThumbnailRequest request,
        CancellationToken cancellationToken,
        Func<TCard, CancellationToken, Task<string?>> generateAsync,
        IAppLogger logger,
        string logPrefix,
        Action<TCard, string>? onGenerated) where TCard : CardBase
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (card.HasThumbnail) return;
            var thumbnailPath = await generateAsync(card, cancellationToken)
                .ConfigureAwait(false);
            if (thumbnailPath != null && !cancellationToken.IsCancellationRequested)
            {
                onGenerated?.Invoke(card, thumbnailPath);
                _dispatcherQueue.TryEnqueue(() =>
                {
                    _thumbnailPathCache[card.FilePath] = thumbnailPath;
                    card.ThumbnailPath = thumbnailPath;
                });
            }
        }
        catch (OperationCanceledException ex) { logger.LogError($"{logPrefix}.GenerateThumbnailCanceled", ex, card.FilePath); }
        catch (Exception ex) { logger.LogError($"{logPrefix}.GenerateThumbnail", ex, card.FilePath); }
        finally
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                CompleteThumbnailRequest(request);
            });
        }
    }

    protected void ResetThumbnailState()
    {
        StartThumbnailSession();
        _thumbnailPathCache.Clear();
        PendingThumbnailCount = 0;
    }

    // Retires the current session before a new one begins: the old source is
    // cancelled and disposed only after the requests it owns have been taken
    // off the scheduler, and the session number moves so late completions from
    // the retired session are ignored. The path cache is deliberately not
    // cleared here; only a full reload discards it.
    private void StartThumbnailSession()
    {
        var previousCts = _thumbnailCts;
        previousCts?.Cancel();
        CancelPendingThumbnailRequests();
        previousCts?.Dispose();
        _thumbnailCts = new CancellationTokenSource();
        _thumbnailSession++;
    }

    /// <summary>
    /// Starts a thumbnail request that belongs to the currently realized item.  A
    /// recycled container cancels this request, so off-screen work cannot occupy the
    /// decode gate ahead of thumbnails the user can actually see.
    /// </summary>
    protected bool TryBeginThumbnailRequest(
        string filePath,
        ThumbnailWorkPriority priority,
        out ThumbnailRequest request)
    {
        var sessionToken = _thumbnailCts?.Token ?? CancellationToken.None;
        if (sessionToken.IsCancellationRequested)
        {
            request = null!;
            return false;
        }

        if (_thumbnailRequests.TryGetValue(filePath, out var current))
        {
            // A visible request supersedes queued prefetch work. A matching or lower
            // priority request is already enough, unless it has been cancelled while
            // the container was recycled and then immediately realized again.
            if (!current.TokenSource.IsCancellationRequested
                && (current.Priority == ThumbnailWorkPriority.Visible
                    || priority == ThumbnailWorkPriority.Prefetch))
            {
                request = null!;
                return false;
            }

            current.TokenSource.Cancel();
            if (current.Handle is { } handle)
                _thumbnailScheduler.Cancel(handle);
        }

        request = new ThumbnailRequest(
            filePath,
            priority,
            _thumbnailSession,
            CancellationTokenSource.CreateLinkedTokenSource(sessionToken));
        _thumbnailRequests[filePath] = request;
        return true;
    }

    protected bool ScheduleThumbnailRequest(
        ThumbnailRequest request,
        Func<CancellationToken, Task> action)
    {
        PendingThumbnailCount++;
        var handle = _thumbnailScheduler.Enqueue(
            request.Priority,
            action,
            request.TokenSource.Token,
            () => _dispatcherQueue.TryEnqueue(() => CompleteThumbnailRequest(request)));
        if (handle is { } scheduled)
        {
            request.Handle = scheduled;
            return true;
        }

        PendingThumbnailCount = Math.Max(0, PendingThumbnailCount - 1);
        if (_thumbnailRequests.TryGetValue(request.FilePath, out var current)
            && ReferenceEquals(current, request))
        {
            _thumbnailRequests.Remove(request.FilePath);
        }
        request.TokenSource.Dispose();
        return false;
    }

    protected void ReleaseThumbnailRequest(string filePath)
    {
        if (!_thumbnailRequests.TryGetValue(filePath, out var request)) return;
        request.TokenSource.Cancel();
        if (request.Handle is { } handle)
            _thumbnailScheduler.Cancel(handle);
    }

    protected void CompleteThumbnailRequest(ThumbnailRequest request)
    {
        request.TokenSource.Dispose();
        if (request.Session == _thumbnailSession)
            PendingThumbnailCount = Math.Max(0, PendingThumbnailCount - 1);

        if (_thumbnailRequests.TryGetValue(request.FilePath, out var current)
            && ReferenceEquals(current, request))
        {
            _thumbnailRequests.Remove(request.FilePath);
        }
    }

    protected CancellationToken BeginLoad()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        return _loadCts.Token;
    }

    protected async Task RunLoadAsync(Func<CancellationToken, Task> load,
        IAppLogger logger, string cancellationOperation, Action? cancelMetadata = null)
    {
        var cancellationToken = BeginLoad();
        var loadSource = _loadCts;
        cancelMetadata?.Invoke();
        ResetThumbnailState();

        IsLoading = true;
        var viewRefreshDeferral = DeferCardsViewRefresh();
        try
        {
            await load(cancellationToken);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogError(cancellationOperation, ex);
        }
        finally
        {
            viewRefreshDeferral.Dispose();
            // Dispose can run while load is awaiting; reading a disposed CTS.Token throws.
            if (ReferenceEquals(_loadCts, loadSource))
                IsLoading = false;
            RaiseCardsReloaded();
        }
    }

    /// <summary>
    /// Holds view notifications while an initial folder scan is adding batches.
    /// Without this deferral, every partial, re-sorted batch can recycle the same
    /// visible GridView containers, making their thumbnail and author bindings
    /// visibly flash until the scan completes.
    /// </summary>
    protected IDisposable DeferCardsViewRefresh() => CardsView.DeferRefresh();

    // For galleries whose scan result needs only path de-duplication before adding.
    // The supplied index uses the caller's existing comparer and belongs to this source.
    protected void PublishScannedCards<TCard>(IEnumerable<TCard> batch,
        Dictionary<string, TCard> index, CancellationToken token) where TCard : CardBase
    {
        token.ThrowIfCancellationRequested();
        PublishScannedBatch(() =>
        {
            foreach (var card in batch)
            {
                if (!index.TryAdd(card.FilePath, card)) continue;
                _cardsSource.Add(card);
            }
        }, token);
    }

    // ScanFoldersAsync invokes this synchronous producer callback on its worker.
    protected void PublishScannedBatch(Action applyBatch, CancellationToken token)
        => AwaitedUiPublication.InvokeBlocking(
            action => _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => action()),
            () => { using (CardsView.DeferRefresh()) applyBatch(); },
            TimeSpan.FromSeconds(10), token);

    public virtual void Activate()
    {
        ActivateThumbnailRequests();
    }

    public void ActivateThumbnailRequests()
    {
        if (_thumbnailCts is not null && !_thumbnailCts.IsCancellationRequested)
            return;

        StartThumbnailSession();
        PendingThumbnailCount = 0;
    }

    // Derived Dispose methods retain their own metadata and event cleanup after this.
    protected void DisposeWorkCancellationSources()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
    }

    public virtual void CancelPendingWork()
    {
        _loadCts?.Cancel();
        _thumbnailCts?.Cancel();
        CancelPendingThumbnailRequests();
    }

    protected void OnShowFileNamesSettingChanged(bool value)
    {
        _dispatcherQueue.TryEnqueue(() => ShowFileNames = value);
    }

    protected void RefreshFilterAndNotify()
    {
        CardsView.RefreshFilter();
        OnPropertyChanged(nameof(IsEmpty));
        RaiseViewRefreshed();
    }

    protected void RaiseCardsReloaded() => CardsReloaded?.Invoke();
    protected void RaiseViewRefreshed() => ViewRefreshed?.Invoke();

    private void CancelPendingThumbnailRequests()
    {
        foreach (var request in _thumbnailRequests.Values)
        {
            request.TokenSource.Cancel();
            if (request.Handle is { } handle)
                _thumbnailScheduler.Cancel(handle);
        }
        _thumbnailRequests.Clear();
    }

    protected sealed class ThumbnailRequest(
        string filePath,
        ThumbnailWorkPriority priority,
        long session,
        CancellationTokenSource tokenSource)
    {
        public string FilePath { get; } = filePath;
        public ThumbnailWorkPriority Priority { get; } = priority;
        public long Session { get; } = session;
        public CancellationTokenSource TokenSource { get; } = tokenSource;
        public ThumbnailWorkHandle? Handle { get; set; }
    }
}

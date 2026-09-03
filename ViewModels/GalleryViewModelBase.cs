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

    private int _shuffleDisplayCount;
    private readonly List<object> _shuffleQueue = [];
    private readonly Dictionary<object, int> _shuffleOrderMap = [];
    private readonly HashSet<object> _shuffleUsedCards = [];

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
                    new SortDescription(SortDirection.Ascending, new ShuffleQueueComparer(_shuffleOrderMap)));
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

    protected void BuildShuffleQueue()
    {
        var candidates = new List<object>();
        foreach (object? card in _cardsSource)
            if (card is not null && CardPassesFilter(card))
                candidates.Add(card);

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int poolSize = Math.Min(ShuffleConstants.PoolSize, candidates.Count);
        _shuffleQueue.Clear();
        _shuffleUsedCards.Clear();
        _shuffleOrderMap.Clear();

        for (int i = 0; i < poolSize; i++)
        {
            _shuffleQueue.Add(candidates[i]);
            _shuffleOrderMap[candidates[i]] = i;
            _shuffleUsedCards.Add(candidates[i]);
        }
    }

    private void AdvanceShuffleQueue()
    {
        int displayCount = Math.Min(_shuffleDisplayCount, _shuffleQueue.Count);
        if (displayCount <= 0 || _shuffleQueue.Count == 0) return;

        var tail = _shuffleQueue.Skip(displayCount).ToList();

        var candidates = new List<object>();
        foreach (object? card in _cardsSource)
            if (card is not null && CardPassesFilter(card) && !_shuffleUsedCards.Contains(card))
                candidates.Add(card);

        if (candidates.Count == 0)
        {
            _shuffleUsedCards.Clear();
            foreach (var item in tail)
                _shuffleUsedCards.Add(item);
            foreach (object? card in _cardsSource)
                if (card is not null && CardPassesFilter(card) && !_shuffleUsedCards.Contains(card))
                    candidates.Add(card);
        }

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int needed = ShuffleConstants.PoolSize - tail.Count;
        int take = Math.Min(needed, candidates.Count);

        _shuffleQueue.Clear();
        _shuffleOrderMap.Clear();
        _shuffleQueue.AddRange(tail);
        for (int i = 0; i < take; i++)
        {
            _shuffleQueue.Add(candidates[i]);
            _shuffleUsedCards.Add(candidates[i]);
        }

        for (int i = 0; i < _shuffleQueue.Count; i++)
            _shuffleOrderMap[_shuffleQueue[i]] = i;
    }

    private void ClearShuffleState()
    {
        _shuffleQueue.Clear();
        _shuffleOrderMap.Clear();
        _shuffleUsedCards.Clear();
    }

    protected bool TryApplyShuffleFilter()
    {
        if (!IsShuffleMode) return false;

        int displayCount = Math.Min(_shuffleDisplayCount, _shuffleQueue.Count);
        var displaySet = new HashSet<object>();
        for (int i = 0; i < displayCount; i++)
            displaySet.Add(_shuffleQueue[i]);

        CardsView.Filter = item => displaySet.Contains(item);
        RefreshFilterAndNotify();
        return true;
    }

    protected void ResetThumbnailState()
    {
        var previousCts = _thumbnailCts;
        previousCts?.Cancel();
        CancelPendingThumbnailRequests();
        previousCts?.Dispose();
        _thumbnailCts = new CancellationTokenSource();
        _thumbnailSession++;
        _thumbnailPathCache.Clear();
        PendingThumbnailCount = 0;
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

    /// <summary>
    /// Holds view notifications while an initial folder scan is adding batches.
    /// Without this deferral, every partial, re-sorted batch can recycle the same
    /// visible GridView containers, making their thumbnail and author bindings
    /// visibly flash until the scan completes.
    /// </summary>
    protected IDisposable DeferCardsViewRefresh() => CardsView.DeferRefresh();

    public virtual void Activate()
    {
        ActivateThumbnailRequests();
    }

    public void ActivateThumbnailRequests()
    {
        if (_thumbnailCts is not null && !_thumbnailCts.IsCancellationRequested)
            return;

        var previousCts = _thumbnailCts;
        previousCts?.Cancel();
        CancelPendingThumbnailRequests();
        previousCts?.Dispose();
        _thumbnailCts = new CancellationTokenSource();
        _thumbnailSession++;
        PendingThumbnailCount = 0;
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

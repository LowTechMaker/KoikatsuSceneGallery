using System.Collections;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI.Collections;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
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

    protected CancellationTokenSource? _loadCts;

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

    public bool HasCompletedLoad { get; protected set; }

    public bool IsEmpty => !IsLoading && CardsView.Count == 0;

    [ObservableProperty]
    public partial bool ShowFileNames { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneratingThumbnails))]
    public partial int PendingThumbnailCount { get; set; }

    public bool IsGeneratingThumbnails => PendingThumbnailCount > 0;

    public event Action? CardsReloaded;
    public event Action? CardsChanged;
    public event Action? ViewRefreshed;

    protected abstract bool CardPassesFilter(object card);
    protected abstract void ApplyFilter();

    protected GalleryViewModelBase(IList cardsSource)
    {
        _cardsSource = cardsSource;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        CardsView = new AdvancedCollectionView(cardsSource, true);
        if (cardsSource is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged += (_, _) =>
            {
                // While a full load is running IsEmpty is false by definition.
                // Raising the same binding notification for thousands of
                // individual cards only consumes the UI thread; IsLoading
                // notifies IsEmpty once when the load finishes.
                if (!IsLoading)
                    OnPropertyChanged(nameof(IsEmpty));
            };
        }
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

    protected CancellationToken BeginLoad()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        HasCompletedLoad = false;
        return _loadCts.Token;
    }

    public virtual void Activate()
    {
    }

    public virtual void CancelPendingWork()
    {
        _loadCts?.Cancel();
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

    protected async Task EnqueueBatchAsync(
        Action action,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        if (!_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    action();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetException(new InvalidOperationException(failureMessage));
        }

        await completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Hands the UI thread back so queued rendering and input run before the
    /// caller continues. A low-priority dispatcher round-trip costs
    /// microseconds, while Task.Delay(1) waits out a full Windows timer tick
    /// (~15 ms) on every call.
    /// </summary>
    protected async Task YieldToUiAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Cancellation has to complete the task itself: once the callback is
        // queued, nothing else guarantees it will ever run — a dispatcher that
        // is shutting down simply drops it. Both completions are Try* calls,
        // so whichever loses the race is a no-op.
        using var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        if (!_dispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                () => completion.TrySetResult()))
        {
            // No dispatcher to yield to. Complete rather than wait forever;
            // the caller re-checks cancellation on its next turn.
            completion.TrySetResult();
        }

        await completion.Task;
    }

    protected void RaiseCardsReloaded()
    {
        HasCompletedLoad = true;
        CardsReloaded?.Invoke();
    }
    protected void RaiseCardsChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        CardsChanged?.Invoke();
    }
    protected void RaiseViewRefreshed() => ViewRefreshed?.Invoke();
}

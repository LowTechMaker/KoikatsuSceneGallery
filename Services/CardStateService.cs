using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// The app's one way to read and change <see cref="CardStateStore"/>: it knows
/// the library roots and keeps hashing off the UI thread, so pages only say
/// which card and which flag.
/// </summary>
internal sealed class CardStateService(CardStateStore store, SettingsViewModel settings, IAppLogger logger)
{
    // One for the app's lifetime: it remembers what it hashed, so a reconcile
    // after every library reload only reads cards it has not seen before.
    private readonly CardStateReconciler _reconciler = new(store, CardPayloadHash.TryCompute);
    // Reloads can come in quick succession; overlapping runs would hash the
    // same cards twice before either remembered them.
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);

    /// <summary>Raised with the card's full path, on a background thread.</summary>
    public event Action<string>? StateChanged
    {
        add => store.StateChanged += value;
        remove => store.StateChanged -= value;
    }

    private string[] Roots() =>
        [.. settings.FolderPaths, .. settings.CharacterFolderPaths, .. settings.CoordinateFolderPaths];

    /// <summary>Reads the state files off the UI thread. Cheap to call again.</summary>
    public Task EnsureLoadedAsync()
    {
        var roots = Roots();
        return Task.Run(() => store.EnsureLoaded(roots));
    }

    /// <summary>The stored state of the card at this path; null when it has none or nothing is loaded yet.</summary>
    public CardState? Get(string filePath) => store.TryGetByPath(filePath);

    public bool Has(string filePath, CardFlag flag) => Get(filePath)?.Has(flag) == true;

    public IReadOnlyList<(string FullPath, CardState State)> Snapshot() => store.Snapshot();

    /// <summary>Sets <paramref name="flag"/> on every card; returns how many could not be saved.</summary>
    public async Task<int> SetAsync(IReadOnlyList<string> filePaths, CardFlag flag, bool value)
    {
        var roots = Roots();
        var hashed = await Task.Run(() =>
        {
            store.EnsureLoaded(roots);
            return filePaths.Select(path => (path, hash: CardPayloadHash.TryCompute(path))).ToArray();
        });

        var failed = 0;
        foreach (var (path, hash) in hashed)
        {
            if (hash is null || !await store.SetFlagAsync(path, hash, flag, value))
                failed++;
        }
        return failed;
    }

    /// <summary>Flips <paramref name="flag"/> on one card; returns the new value, or null when it could not be saved.</summary>
    public async Task<bool?> ToggleAsync(string filePath, CardFlag flag)
    {
        var value = !Has(filePath, flag);
        return await SetAsync([filePath], flag, value) == 0 ? value : null;
    }

    /// <summary>The rating the card at this path was filed under, or null when none is recorded or nothing is loaded yet.</summary>
    public int? FiledRatingOf(string filePath) => Get(filePath)?.FiledRating;

    /// <summary>
    /// Records the rating the card was filed under, loading the state files
    /// first. False when it could not be stored. <paramref name="hash"/> is
    /// computed when not given.
    /// </summary>
    public async Task<bool> SetFiledRatingAsync(string filePath, string? hash, int? rating)
    {
        var roots = Roots();
        return await Task.Run(async () =>
        {
            store.EnsureLoaded(roots);
            return (hash ?? CardPayloadHash.TryCompute(filePath)) is { } known
                   && await store.SetFiledRatingAsync(filePath, known, rating).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Records the filed rating of many cards, hashing them off the UI thread
    /// and writing each state file once. Returns how many could not be stored
    /// (not a card, outside every library root, or a read-only root).
    /// </summary>
    public Task<int> SetFiledRatingsAsync(IReadOnlyList<(string FilePath, int Rating)> ratings)
    {
        var roots = Roots();
        return Task.Run(async () =>
        {
            store.EnsureLoaded(roots);
            var hashed = ratings
                .Select(r => (r.FilePath, Hash: CardPayloadHash.TryCompute(r.FilePath), r.Rating))
                .ToList();
            var notCards = hashed.Count(r => r.Hash is null);
            var failed = await store.SetFiledRatingsAsync(
                [.. hashed.Where(r => r.Hash is not null).Select(r => (r.FilePath, r.Hash!, r.Rating))]).ConfigureAwait(false);
            return notCards + failed;
        });
    }

    /// <summary>
    /// Files the rating of every card an import just committed, when it is
    /// above G (<see cref="ImportFiledRatings"/>). New folders have no rating
    /// folder, so this is what keeps an imported R-18 card from reading as G.
    /// Failures are logged; the import itself has succeeded.
    /// </summary>
    public async Task FileImportedRatingsAsync(IReadOnlyList<string> committedPaths, IEnumerable<Models.ImportItem> items)
    {
        var filed = ImportFiledRatings.ForCommitted(committedPaths,
            items.Select(item => (item.DestinationPath, (int)item.Rating)).ToList());
        if (filed.Count == 0) return;
        try
        {
            var failed = await SetFiledRatingsAsync(filed).ConfigureAwait(false);
            if (failed > 0)
                logger.LogError("CardState.FileImportedRatings",
                    new IOException($"{failed} of {filed.Count} imported ratings could not be recorded."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError("CardState.FileImportedRatings", ex);
        }
    }

    /// <summary>
    /// For the library migration: files each card's folder-implied rating at
    /// its current (old) path, unless it already has one at least as high, and
    /// returns the paths that could not be filed (not a card, or a store that
    /// would not take it). Those cards must not leave their rating folder.
    /// </summary>
    public Task<IReadOnlyList<string>> RecordFolderRatingsAsync(IReadOnlyList<(string Path, int Rating)> ratings)
    {
        var roots = Roots();
        return Task.Run(async () =>
        {
            store.EnsureLoaded(roots);
            var failed = new List<string>();
            var batch = new List<(string, string, int)>(ratings.Count);
            foreach (var (path, rating) in ratings)
            {
                if (store.TryGetByPath(path)?.FiledRating is { } filed && filed >= rating) continue;
                if (CardPayloadHash.TryCompute(path) is { } hash) batch.Add((path, hash, rating));
                else failed.Add(path);
            }
            failed.AddRange(await store.SetFiledRatingsReportingAsync(batch).ConfigureAwait(false));
            return (IReadOnlyList<string>)failed;
        });
    }

    /// <summary>Re-points the states of cards the app moved, by their old paths; one write per root.</summary>
    public Task<int> MovePathsAsync(IReadOnlyList<(string From, string To)> moves)
    {
        var roots = Roots();
        return Task.Run(async () =>
        {
            store.EnsureLoaded(roots);
            return await store.MovePathsAsync(moves).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// The app just moved this card (its hash is known): point its state at
    /// the new path now rather than waiting for the next reconcile.
    /// </summary>
    public Task ResolveMovedAsync(string newPath, string hash)
    {
        var roots = Roots();
        return Task.Run(async () =>
        {
            store.EnsureLoaded(roots);
            await store.ResolveAsync(newPath, hash).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Re-attaches states whose card was moved or renamed outside the app to
    /// the card in <paramref name="livePaths"/> with the same hash, off the UI
    /// thread. Each re-attached card raises <see cref="StateChanged"/>.
    /// Returns how many were re-attached.
    /// </summary>
    public Task<int> ReconcileAsync(IReadOnlyCollection<string> livePaths, CancellationToken cancellationToken = default)
    {
        var roots = Roots();
        return Task.Run(async () =>
        {
            await _reconcileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                store.EnsureLoaded(roots);
                return await _reconciler.ReconcileAsync(livePaths, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _reconcileGate.Release();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Records the cards as used. Called from drag and copy handlers, which
    /// run on the UI thread and must not wait for disk: this returns at once.
    /// </summary>
    public void MarkUsed(IReadOnlyList<string> filePaths)
    {
        var roots = Roots();
        _ = Task.Run(async () =>
        {
            store.EnsureLoaded(roots);
            foreach (var path in filePaths)
            {
                if (CardPayloadHash.TryCompute(path) is { } hash)
                    await store.MarkUsedAsync(path, hash).ConfigureAwait(false);
            }
        }).ContinueWith(
            task => logger.LogError("CardState.MarkUsed", task.Exception!.GetBaseException()),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}

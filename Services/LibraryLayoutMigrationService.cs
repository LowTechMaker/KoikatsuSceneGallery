using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// The app's side of <see cref="LibraryLayoutMigration"/>: builds the options
/// from settings and plugins, keeps the rest of the app out of the way while
/// files move, and brings it back up to date afterwards.
/// </summary>
/// <remarks>
/// Everything that decides what moves, and every guard, is in Core and tested
/// there. This class only gathers inputs and does the bookkeeping around a run:
/// <list type="bullet">
/// <item>refuses while an import, a local import or a gallery scan is running;</item>
/// <item>stops the three folder watchers for the run, so thousands of moves do
/// not become thousands of change notifications;</item>
/// <item>re-points the path-keyed stores (card states, the user's ratings) in
/// one write per root, clears the character-notes cache, rescans local sources,
/// invalidates the import library cache, and reloads the three galleries once,
/// which also restarts the watchers.</item>
/// </list>
/// </remarks>
internal sealed class LibraryLayoutMigrationService(
    ImportService importService,
    CardStateService cardStates,
    UserCardRatingStore ratings,
    CharacterAnnotationStore annotations,
    LocalSourceRegistry localSources,
    LibraryRegistry libraries,
    SceneCardService scenes,
    CharacterCardService characters,
    CoordinateCardService coordinates,
    Func<bool> importBusy,
    IAppLogger logger)
{
    private int _running;

    /// <summary>Why a run cannot start now (a string key), or null when it can.</summary>
    public string? BusyReason()
        => Volatile.Read(ref _running) != 0 || importBusy() || libraries.All.Any(library => library.ViewModel.IsLoading)
            ? "LayoutMigration_AppBusy"
            : null;

    /// <summary>Plans the run off the UI thread. Changes nothing on disk.</summary>
    public async Task<LayoutMigrationPlan> PlanAsync(CancellationToken ct)
    {
        var options = await importService.BuildLayoutMigrationOptionsAsync().ConfigureAwait(false);
        return await Task.Run(() => LibraryLayoutMigration.Plan(options, ct), ct).ConfigureAwait(false);
    }

    /// <summary>Unfinished runs (to resume) and unfinished rollbacks (to finish), across every root.</summary>
    public async Task<IReadOnlyList<string>> FindUnfinishedAsync()
    {
        var options = await importService.BuildLayoutMigrationOptionsAsync().ConfigureAwait(false);
        return await Task.Run(() => LibraryLayoutMigration.FindUnfinished(options.Roots)).ConfigureAwait(false);
    }

    /// <summary>The run each root can move back now (newest only), newest last.</summary>
    public async Task<IReadOnlyList<LayoutMigrationRun>> FindUndoableAsync()
    {
        var options = await importService.BuildLayoutMigrationOptionsAsync().ConfigureAwait(false);
        return await Task.Run(() => LibraryLayoutMigration.FindUndoable(options.Roots)).ConfigureAwait(false);
    }

    public Task<LayoutMigrationResult> ApplyAsync(
        LayoutMigrationPlan plan, IProgress<LayoutMigrationProgress>? progress, CancellationToken ct)
        => RunAsync(() => LibraryLayoutMigration.ApplyAsync(plan, Steps(), progress, ct));

    /// <summary>Moves back everything the given journals moved, one root at a time.</summary>
    public Task<LayoutMigrationResult> RollbackAsync(
        IReadOnlyList<string> journals, IProgress<LayoutMigrationProgress>? progress, CancellationToken ct)
        => RunAsync(async () =>
        {
            var moved = 0;
            var stayed = new List<LayoutStay>();
            var stopped = false;
            foreach (var journal in journals)
            {
                var result = await LibraryLayoutMigration.RollbackAsync(journal, Steps(), progress, ct).ConfigureAwait(false);
                moved += result.Moved;
                stayed.AddRange(result.Stayed);
                if (result.Stopped) { stopped = true; break; }
            }
            return new LayoutMigrationResult(moved, stayed, stopped, journals);
        });

    private LayoutMigrationSteps Steps() => new(
        Mover: new LibraryCardMover(),
        RecordRatings: (pairs, _) => cardStates.RecordFolderRatingsAsync(pairs),
        PathsMoved: async (pairs, _) =>
        {
            await cardStates.MovePathsAsync(pairs).ConfigureAwait(false);
            await ratings.MovePathsAsync(pairs).ConfigureAwait(false);
        });

    private async Task<LayoutMigrationResult> RunAsync(Func<Task<LayoutMigrationResult>> run)
    {
        if (BusyReason() is { } busy) throw new InvalidOperationException(busy);
        Interlocked.Exchange(ref _running, 1);
        scenes.StopWatching();
        characters.StopWatching();
        coordinates.StopWatching();
        try
        {
            // The files move on the thread pool; the caller's (UI) context is
            // kept for the reload below, which updates the galleries' collections.
            return await Task.Run(run);
        }
        finally
        {
            try
            {
                annotations.ClearCache();
                importService.InvalidateLibraryFileCache();
                localSources.BeginRescan();
                // Each load rescans its folders and starts watching them again.
                foreach (var library in libraries.All)
                    await library.LoadAsync();
            }
            catch (Exception ex)
            {
                logger.LogError("LayoutMigration.Refresh", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }
    }
}

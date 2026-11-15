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

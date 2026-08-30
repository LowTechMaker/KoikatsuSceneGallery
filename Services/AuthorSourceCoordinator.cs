using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

public sealed class AuthorSourceCoordinator
{
    private readonly AuthorInfoService _authorInfoService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly GalleryViewModel _galleryViewModel;
    private readonly CharacterGalleryViewModel _characterGalleryViewModel;
    private readonly CoordinateGalleryViewModel _coordinateGalleryViewModel;
    private readonly FriendService _friendService;
    private readonly IAppLogger _logger;
    private readonly Dictionary<FriendLibrarySourceKinds, HashSet<string>>
        _loadedSourceSets = [];
    private Task? _warmupTask;

    public AuthorSourceCoordinator(
        AuthorInfoService authorInfoService,
        SettingsViewModel settingsViewModel,
        GalleryViewModel galleryViewModel,
        CharacterGalleryViewModel characterGalleryViewModel,
        CoordinateGalleryViewModel coordinateGalleryViewModel,
        FriendService friendService,
        IAppLogger logger)
    {
        _authorInfoService = authorInfoService;
        _settingsViewModel = settingsViewModel;
        _galleryViewModel = galleryViewModel;
        _characterGalleryViewModel = characterGalleryViewModel;
        _coordinateGalleryViewModel = coordinateGalleryViewModel;
        _friendService = friendService;
        _logger = logger;
    }

    public void Initialize()
    {
        _friendService.LibrarySourcesChanged += OnFriendLibrarySourcesChanged;
        _settingsViewModel.SceneFolderPathsChanged +=
            OnSceneFolderPathsChanged;
        _settingsViewModel.CharacterFolderPathsChanged +=
            OnCharacterFolderPathsChanged;
        _settingsViewModel.CoordinateFolderPathsChanged +=
            OnCoordinateFolderPathsChanged;

        if (!_authorInfoService.IsAvailable)
            return;

        ApplyLibraryRoots();
        _authorInfoService.Attach(_galleryViewModel.Cards, AuthorCardKind.Scene);
        _authorInfoService.Attach(_characterGalleryViewModel.Cards, AuthorCardKind.Character);
        _authorInfoService.Attach(_coordinateGalleryViewModel.Cards, AuthorCardKind.Coordinate);
    }

    public void Refresh(bool reloadLoadedSources = false)
    {
        if (!_authorInfoService.IsAvailable)
            return;

        ApplyLibraryRoots();
        _authorInfoService.RebuildAssignments(
            _galleryViewModel.Cards,
            _characterGalleryViewModel.Cards,
            _coordinateGalleryViewModel.Cards);
        if (reloadLoadedSources)
        {
            QueueSourceReload(
                FriendLibrarySourceKinds.All,
                "AuthorSource.Refresh");
            return;
        }

        _warmupTask = LoadAsync(
            forceReload: false,
            kinds: FriendLibrarySourceKinds.All);
        _warmupTask.Observe(_logger, "AuthorSource.Refresh");
    }

    public Task EnsureLoadedAsync()
    {
        if (!_authorInfoService.IsAvailable)
            return Task.CompletedTask;

        if (_warmupTask is { IsCompleted: false })
            return _warmupTask;

        _warmupTask = LoadAsync(
            forceReload: false,
            kinds: FriendLibrarySourceKinds.All);
        return _warmupTask;
    }

    private void OnSceneFolderPathsChanged() =>
        OnFolderPathsChanged(FriendLibrarySourceKinds.Scene);

    private void OnCharacterFolderPathsChanged() =>
        OnFolderPathsChanged(FriendLibrarySourceKinds.Character);

    private void OnCoordinateFolderPathsChanged() =>
        OnFolderPathsChanged(FriendLibrarySourceKinds.Coordinate);

    private void OnFolderPathsChanged(FriendLibrarySourceKinds kinds)
    {
        if (_authorInfoService.IsAvailable)
        {
            ApplyLibraryRoots();
            _authorInfoService.RebuildAssignments(
                _galleryViewModel.Cards,
                _characterGalleryViewModel.Cards,
                _coordinateGalleryViewModel.Cards);
        }

        QueueSourceReload(kinds, "AuthorSource.ReloadSettingsSources");
    }

    private void OnFriendLibrarySourcesChanged(FriendLibrarySourceKinds kinds)
    {
        if (kinds == FriendLibrarySourceKinds.None)
            return;

        QueueSourceReload(
            kinds,
            "AuthorSource.ReloadFriendSources");
    }

    private void QueueSourceReload(
        FriendLibrarySourceKinds kinds,
        string operation)
    {
        var previousLoad = _warmupTask;
        _warmupTask = ReloadSourcesAsync(previousLoad, kinds, operation);
    }

    private void ApplyLibraryRoots()
        => _authorInfoService.UpdateRoots(
            [.. _settingsViewModel.FolderPaths, .. _settingsViewModel.CharacterFolderPaths, .. _settingsViewModel.CoordinateFolderPaths]);

    private async Task ReloadSourcesAsync(
        Task? previousLoad,
        FriendLibrarySourceKinds kinds,
        string operation)
    {
        if (previousLoad is not null)
        {
            try
            {
                await previousLoad;
            }
            catch
            {
                // The task's original owner records the error. A previous
                // failure must not poison the serialized reload queue.
            }
        }

        var changedKinds = await GetChangedSourceKindsAsync(kinds);
        if (changedKinds == FriendLibrarySourceKinds.None)
            return;

        try
        {
            await LoadAsync(forceReload: true, kinds: changedKinds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                operation,
                ex,
                changedKinds.ToString());
        }
    }

    private async Task LoadAsync(
        bool forceReload,
        FriendLibrarySourceKinds kinds)
    {
        var sourceSets = CaptureSourceSets(
            kinds,
            await _friendService.CaptureLibrarySnapshotAsync());
        using var notificationDeferral = _authorInfoService.DeferNotifications();
        if (kinds.HasFlag(FriendLibrarySourceKinds.Scene))
        {
            await AwaitOrLoadAsync(
                _galleryViewModel.HasCompletedLoad,
                _galleryViewModel.IsLoading,
                _galleryViewModel.LoadCardsCommand,
                forceReload);
            await SaveCompletedSourceSetsAsync(
                FriendLibrarySourceKinds.Scene,
                sourceSets);
        }
        if (kinds.HasFlag(FriendLibrarySourceKinds.Character))
        {
            await AwaitOrLoadAsync(
                _characterGalleryViewModel.HasCompletedLoad,
                _characterGalleryViewModel.IsLoading,
                _characterGalleryViewModel.LoadCardsCommand,
                forceReload);
            await SaveCompletedSourceSetsAsync(
                FriendLibrarySourceKinds.Character,
                sourceSets);
        }
        if (kinds.HasFlag(FriendLibrarySourceKinds.Coordinate))
        {
            await AwaitOrLoadAsync(
                _coordinateGalleryViewModel.HasCompletedLoad,
                _coordinateGalleryViewModel.IsLoading,
                _coordinateGalleryViewModel.LoadCardsCommand,
                forceReload);
            await SaveCompletedSourceSetsAsync(
                FriendLibrarySourceKinds.Coordinate,
                sourceSets);
        }
    }

    private async Task<FriendLibrarySourceKinds> GetChangedSourceKindsAsync(
        FriendLibrarySourceKinds candidates)
    {
        var current = CaptureSourceSets(
            candidates,
            await _friendService.CaptureLibrarySnapshotAsync());
        var changed = FriendLibrarySourceKinds.None;
        foreach (var kind in EnumerateKinds(candidates))
        {
            if (!HasCompletedLoad(kind)
                || !_loadedSourceSets.TryGetValue(kind, out var loaded)
                || !loaded.SetEquals(current[kind]))
            {
                changed |= kind;
            }
        }

        return changed;
    }

    /// <summary>
    /// Builds the source sets from an already-captured snapshot. Every
    /// filesystem check happened while the snapshot was taken, so this is
    /// memory-only and safe to run on the UI thread.
    /// </summary>
    private Dictionary<FriendLibrarySourceKinds, HashSet<string>>
        CaptureSourceSets(
            FriendLibrarySourceKinds kinds,
            LinkedLibrarySnapshot snapshot)
    {
        var result =
            new Dictionary<FriendLibrarySourceKinds, HashSet<string>>();
        if (kinds.HasFlag(FriendLibrarySourceKinds.Scene))
        {
            result[FriendLibrarySourceKinds.Scene] = FriendSourceSet.Build(
                _settingsViewModel.FolderPaths.Concat(snapshot.SceneFolders),
                snapshot.SceneCardPaths);
        }
        if (kinds.HasFlag(FriendLibrarySourceKinds.Character))
        {
            result[FriendLibrarySourceKinds.Character] =
                FriendSourceSet.Build(
                    _settingsViewModel.CharacterFolderPaths.Concat(
                        snapshot.CharacterFolders),
                    snapshot.CharacterCardPaths);
        }
        if (kinds.HasFlag(FriendLibrarySourceKinds.Coordinate))
        {
            result[FriendLibrarySourceKinds.Coordinate] =
                FriendSourceSet.Build(
                    _settingsViewModel.CoordinateFolderPaths.Concat(
                        snapshot.CoordinateFolders),
                    snapshot.CoordinateCardPaths);
        }

        return result;
    }

    private async Task SaveCompletedSourceSetsAsync(
        FriendLibrarySourceKinds loadedKinds,
        IReadOnlyDictionary<FriendLibrarySourceKinds, HashSet<string>>
            sourceSets)
    {
        // Re-capture so a source that changed while the gallery was loading is
        // not recorded as loaded. The snapshot must be fresh for that check to
        // mean anything, so it is taken here rather than reused.
        var currentSourceSets = CaptureSourceSets(
            loadedKinds,
            await _friendService.CaptureLibrarySnapshotAsync());
        foreach (var kind in EnumerateKinds(loadedKinds))
        {
            if (HasCompletedLoad(kind)
                && sourceSets[kind].SetEquals(currentSourceSets[kind]))
            {
                _loadedSourceSets[kind] = sourceSets[kind];
            }
        }
    }

    private bool HasCompletedLoad(FriendLibrarySourceKinds kind) =>
        kind switch
        {
            FriendLibrarySourceKinds.Scene =>
                _galleryViewModel.HasCompletedLoad,
            FriendLibrarySourceKinds.Character =>
                _characterGalleryViewModel.HasCompletedLoad,
            FriendLibrarySourceKinds.Coordinate =>
                _coordinateGalleryViewModel.HasCompletedLoad,
            _ => false,
        };

    private static IEnumerable<FriendLibrarySourceKinds> EnumerateKinds(
        FriendLibrarySourceKinds kinds)
    {
        if (kinds.HasFlag(FriendLibrarySourceKinds.Scene))
            yield return FriendLibrarySourceKinds.Scene;
        if (kinds.HasFlag(FriendLibrarySourceKinds.Character))
            yield return FriendLibrarySourceKinds.Character;
        if (kinds.HasFlag(FriendLibrarySourceKinds.Coordinate))
            yield return FriendLibrarySourceKinds.Coordinate;
    }

    private static async Task AwaitOrLoadAsync(
        bool hasCompletedLoad,
        bool isLoading,
        IAsyncRelayCommand command,
        bool forceReload)
    {
        if (!forceReload && hasCompletedLoad)
            return;
        if (isLoading)
        {
            var currentLoad = command.ExecutionTask ?? Task.CompletedTask;
            if (!forceReload)
            {
                await currentLoad;
                return;
            }

            await ReloadAfterAsync(currentLoad, command);
        }
        else
            await command.ExecuteAsync(null);
    }

    private static async Task ReloadAfterAsync(
        Task currentLoad,
        IAsyncRelayCommand command)
    {
        try
        {
            await currentLoad;
        }
        catch
        {
            // The current load's original owner records the failure.
            // A requested source reload must still get its own attempt.
        }

        await command.ExecuteAsync(null);
    }
}

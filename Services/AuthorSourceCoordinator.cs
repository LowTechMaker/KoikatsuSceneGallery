using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery.Services;

public sealed class AuthorSourceCoordinator
{
    private readonly AuthorInfoService _authorInfoService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly GalleryViewModel _galleryViewModel;
    private readonly CharacterGalleryViewModel _characterGalleryViewModel;
    private readonly CoordinateGalleryViewModel _coordinateGalleryViewModel;
    private Task? _warmupTask;

    public AuthorSourceCoordinator(
        AuthorInfoService authorInfoService,
        SettingsViewModel settingsViewModel,
        GalleryViewModel galleryViewModel,
        CharacterGalleryViewModel characterGalleryViewModel,
        CoordinateGalleryViewModel coordinateGalleryViewModel)
    {
        _authorInfoService = authorInfoService;
        _settingsViewModel = settingsViewModel;
        _galleryViewModel = galleryViewModel;
        _characterGalleryViewModel = characterGalleryViewModel;
        _coordinateGalleryViewModel = coordinateGalleryViewModel;
    }

    public void Initialize()
    {
        if (!_authorInfoService.IsAvailable)
            return;

        ApplyLibraryRoots();
        _authorInfoService.Attach(_galleryViewModel.Cards, AuthorCardKind.Scene);
        _authorInfoService.Attach(_characterGalleryViewModel.Cards, AuthorCardKind.Character);
        _authorInfoService.Attach(_coordinateGalleryViewModel.Cards, AuthorCardKind.Coordinate);
        _settingsViewModel.SceneFolderPathsChanged += OnFolderPathsChanged;
        _settingsViewModel.CharacterFolderPathsChanged += OnFolderPathsChanged;
        _settingsViewModel.CoordinateFolderPathsChanged += OnFolderPathsChanged;
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
        _warmupTask = LoadAsync(reloadLoadedSources);
    }

    public Task EnsureLoadedAsync()
    {
        if (!_authorInfoService.IsAvailable)
            return Task.CompletedTask;

        if (_warmupTask is { IsCompleted: false })
            return _warmupTask;

        _warmupTask = LoadAsync();
        return _warmupTask;
    }

    private void OnFolderPathsChanged() => Refresh(reloadLoadedSources: true);

    private void ApplyLibraryRoots()
        => _authorInfoService.UpdateRoots(
            [.. _settingsViewModel.FolderPaths, .. _settingsViewModel.CharacterFolderPaths, .. _settingsViewModel.CoordinateFolderPaths]);

    private async Task LoadAsync(bool forceReload = false)
    {
        var loadTasks = new List<Task>(3);
        AwaitOrLoad(loadTasks, _galleryViewModel.Cards.Count, _galleryViewModel.IsLoading, _galleryViewModel.LoadCardsCommand, forceReload);
        AwaitOrLoad(loadTasks, _characterGalleryViewModel.Cards.Count, _characterGalleryViewModel.IsLoading, _characterGalleryViewModel.LoadCardsCommand, forceReload);
        AwaitOrLoad(loadTasks, _coordinateGalleryViewModel.Cards.Count, _coordinateGalleryViewModel.IsLoading, _coordinateGalleryViewModel.LoadCardsCommand, forceReload);

        if (loadTasks.Count > 0)
            await Task.WhenAll(loadTasks);
    }

    private static void AwaitOrLoad(
        List<Task> tasks,
        int cardCount,
        bool isLoading,
        IAsyncRelayCommand command,
        bool forceReload)
    {
        if (!forceReload && cardCount > 0) return;
        if (isLoading)
            tasks.Add(command.ExecutionTask ?? Task.CompletedTask);
        else
            tasks.Add(command.ExecuteAsync(null));
    }
}

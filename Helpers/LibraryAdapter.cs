using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery.Helpers;

public enum LibraryKind { Scenes, Characters, Coordinates, Screenshots }

internal sealed class LibraryAdapter(LibraryKind kind)
{
    public LibraryKind Kind { get; } = kind;
    public GalleryViewModelBase ViewModel { get; } = kind switch
    {
        LibraryKind.Scenes => App.Services.GetRequiredService<GalleryViewModel>(),
        LibraryKind.Characters => App.Services.GetRequiredService<CharacterGalleryViewModel>(),
        LibraryKind.Coordinates => App.Services.GetRequiredService<CoordinateGalleryViewModel>(),
        _ => App.Services.GetRequiredService<MediaGalleryViewModel>("screenshots")
    };
    public IReadOnlyList<CardBase> Cards => ViewModel switch
    {
        GalleryViewModel vm => vm.Cards,
        CharacterGalleryViewModel vm => vm.Cards,
        CoordinateGalleryViewModel vm => vm.Cards,
        MediaGalleryViewModel vm => vm.Cards,
        _ => []
    };
    public string Title => UiText.Get(Kind switch
    {
        LibraryKind.Scenes => "Gallery_Title.Text", LibraryKind.Characters => "Character_Title.Text",
        LibraryKind.Coordinates => "Coordinate_Title.Text", _ => "Screenshot_Title.Text"
    });
    public double ImageRatio => Kind is LibraryKind.Characters or LibraryKind.Coordinates ? 352.0 / 252 : 135.0 / 240;
    public bool GroupingEnabled => Kind != LibraryKind.Screenshots;
    public bool IsParsing => ViewModel switch
    {
        GalleryViewModel vm => vm.IsParsingMetadata, CharacterGalleryViewModel vm => vm.IsParsingMetadata,
        CoordinateGalleryViewModel vm => vm.IsParsingMetadata, _ => false
    };
    public Task LoadAsync() => ViewModel switch
    {
        GalleryViewModel vm => vm.LoadCardsCommand.ExecuteAsync(null),
        CharacterGalleryViewModel vm => vm.LoadCardsCommand.ExecuteAsync(null),
        CoordinateGalleryViewModel vm => vm.LoadCardsCommand.ExecuteAsync(null),
        MediaGalleryViewModel vm => vm.LoadCardsCommand.ExecuteAsync(null), _ => Task.CompletedTask
    };
    public void Thumbnail(CardBase card, bool release = false)
    {
        switch (ViewModel, card)
        {
            case (GalleryViewModel vm, SceneCard c): if (release) vm.ReleaseThumbnail(c); else vm.RequestThumbnail(c); break;
            case (CharacterGalleryViewModel vm, CharacterCard c): if (release) vm.ReleaseThumbnail(c); else vm.RequestThumbnail(c); break;
            case (CoordinateGalleryViewModel vm, CoordinateCard c): if (release) vm.ReleaseThumbnail(c); else vm.RequestThumbnail(c); break;
            case (MediaGalleryViewModel vm, MediaCard c): if (release) vm.ReleaseThumbnail(c); else vm.RequestThumbnail(c); break;
        }
    }
    public void WatchFolders(Action callback)
    {
        var settings = App.Services.GetRequiredService<SettingsViewModel>();
        switch (Kind)
        {
            case LibraryKind.Scenes: settings.SceneFolderPathsChanged += callback; break;
            case LibraryKind.Characters: settings.CharacterFolderPathsChanged += callback; break;
            case LibraryKind.Coordinates: settings.CoordinateFolderPathsChanged += callback; break;
            default: settings.ScreenshotFolderPathsChanged += callback; break;
        }
    }
}

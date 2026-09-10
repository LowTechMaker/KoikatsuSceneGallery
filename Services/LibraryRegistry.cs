using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery.Services;

/// <summary>Application-owned library bindings, in navigation order.</summary>
internal sealed class LibraryRegistry
{
    private readonly IReadOnlyDictionary<LibraryKind, ILibrary> _byKind;
    public IReadOnlyList<ILibrary> All { get; }

    internal LibraryRegistry(IEnumerable<ILibrary> libraries)
    {
        All = Array.AsReadOnly(libraries.ToArray());
        _byKind = All.ToDictionary(library => library.Kind);
    }

    public ILibrary Get(LibraryKind kind) => _byKind.TryGetValue(kind, out var library)
        ? library : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Library is not registered.");

    public ILibrary ForCard(CardBase card) => All.FirstOrDefault(library => library.Supports(card))
        ?? throw new ArgumentException("Card type has no registered library.", nameof(card));

    public static LibraryRegistry Create(SettingsViewModel settings, GalleryViewModel scenes,
        CharacterGalleryViewModel characters, CoordinateGalleryViewModel coordinates,
        MediaGalleryViewModel screenshots) => new([
        new LibraryAdapter<SceneCard>(LibraryKind.Scenes, scenes, scenes.Cards,
            "Gallery_Title.Text", 135.0 / 240, true, () => scenes.IsParsingMetadata,
            () => scenes.LoadCardsCommand.ExecuteAsync(null), card => scenes.RequestThumbnail(card), scenes.ReleaseThumbnail,
            callback => settings.SceneFolderPathsChanged += callback),
        new LibraryAdapter<CharacterCard>(LibraryKind.Characters, characters, characters.Cards,
            "Character_Title.Text", 352.0 / 252, true, () => characters.IsParsingMetadata,
            () => characters.LoadCardsCommand.ExecuteAsync(null), card => characters.RequestThumbnail(card), characters.ReleaseThumbnail,
            callback => settings.CharacterFolderPathsChanged += callback),
        new LibraryAdapter<CoordinateCard>(LibraryKind.Coordinates, coordinates, coordinates.Cards,
            "Coordinate_Title.Text", 352.0 / 252, true, () => coordinates.IsParsingMetadata,
            () => coordinates.LoadCardsCommand.ExecuteAsync(null), card => coordinates.RequestThumbnail(card), coordinates.ReleaseThumbnail,
            callback => settings.CoordinateFolderPathsChanged += callback),
        new LibraryAdapter<MediaCard>(LibraryKind.Screenshots, screenshots, screenshots.Cards,
            "Screenshot_Title.Text", 135.0 / 240, false, () => false,
            () => screenshots.LoadCardsCommand.ExecuteAsync(null), card => screenshots.RequestThumbnail(card), screenshots.ReleaseThumbnail,
            callback => settings.ScreenshotFolderPathsChanged += callback)
    ]);
}

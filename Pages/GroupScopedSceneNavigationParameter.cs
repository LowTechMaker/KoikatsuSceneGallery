using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Pages;

public sealed record GroupScopedSceneNavigationParameter(SceneCard Card, IReadOnlyList<SceneCard> Cards);

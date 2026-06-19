using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public static class SceneClassifier
{
    public static SceneMetadata Classify(ParsedScene? parsed)
    {
        if (parsed is null) return SceneMetadata.Unknown;
        return new SceneMetadata(parsed.Value.Game);
    }
}

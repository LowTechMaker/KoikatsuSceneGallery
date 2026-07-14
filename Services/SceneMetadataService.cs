using System.Text.Json;
using System.Text.Json.Serialization;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public sealed class SceneMetadataService : MetadataCacheService<SceneCard, SceneMetadata>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public SceneMetadataService(IAppLogger logger) : base(logger, "scene_metadata.json", JsonOptions) { }

    protected override SceneMetadata Parse(SceneCard card)
    {
        var parsed = SceneMetadataParser.TryParse(card.FilePath);
        return SceneClassifier.Classify(parsed);
    }
}

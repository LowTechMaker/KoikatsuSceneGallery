using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public sealed class CoordinateMetadataService : MetadataCacheService<CoordinateCard, CoordinateMetadata>
{
    public CoordinateMetadataService(IAppLogger logger) : base(logger, "coord_metadata.json") { }

    protected override CoordinateMetadata Parse(CoordinateCard card)
        => CoordinateCardParser.TryParse(card.FilePath) ?? CoordinateMetadata.Unknown;
}

using System.Text.Json;
using System.Text.Json.Serialization;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public sealed class CharacterMetadataService : MetadataCacheService<CharacterCard, CharacterMetadata>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public CharacterMetadataService(IAppLogger logger) : base(logger, "chara_metadata.json", JsonOptions) { }

    protected override CharacterMetadata Parse(CharacterCard card)
        => CharacterCardParser.TryParse(card.FilePath) ?? CharacterMetadata.Unknown;
}

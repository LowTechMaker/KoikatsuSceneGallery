namespace KoikatsuSceneGallery.Models;

public abstract class CardOwnerRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    public string? SceneFolderPath { get; set; }
    public string? CharacterFolderPath { get; set; }
    public string? CoordinateFolderPath { get; set; }

    public string? AvatarPath { get; set; }
    public string? AvatarAuthorProviderId { get; set; }
    public string? AvatarAuthorId { get; set; }

    public List<string> CardPaths { get; set; } = [];
}

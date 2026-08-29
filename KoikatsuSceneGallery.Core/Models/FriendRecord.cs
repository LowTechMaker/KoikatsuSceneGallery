namespace KoikatsuSceneGallery.Models;

public sealed class FriendRecord : CardOwnerRecord
{
    // Kept for migrating the first friend-folder format.
    public string? FolderPath { get; set; }
}

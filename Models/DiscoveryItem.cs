namespace KoikatsuSceneGallery.Models;

/// <summary>Unique identity for each occurrence, including repeated cards in later rounds.</summary>
public sealed record DiscoveryItem(SceneCard Card, int Round);

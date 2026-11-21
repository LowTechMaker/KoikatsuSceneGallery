using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.ViewModels;

/// <summary>
/// A card the import held back for the user to decide on.
/// </summary>
/// <param name="Destination">Where the card would go.</param>
/// <param name="ExistingPath">
/// For <see cref="ImportConflictKind.NameClash"/>, the different card at the destination;
/// for <see cref="ImportConflictKind.InLibraryElsewhere"/>, the library's copy of this card.
/// </param>
public sealed record ImportConflict(ImportItem Item, ImportConflictKind Kind, string Destination, string ExistingPath);

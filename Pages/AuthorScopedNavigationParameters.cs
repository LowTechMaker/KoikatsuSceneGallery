using KoikatsuSceneGallery.Models;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Pages;

/// <summary>
/// Carries the author context when opening an item from an author page. Detail
/// pages use it to keep previous/next/random navigation inside that author.
/// </summary>
public sealed class AuthorScopedSceneNavigationParameter(SceneCard card, AuthorKey authorKey)
{
    public SceneCard Card { get; } = card;
    public AuthorKey AuthorKey { get; } = authorKey;
}

public sealed class AuthorScopedCharacterNavigationParameter(CharacterCard card, AuthorKey authorKey)
{
    public CharacterCard Card { get; } = card;
    public AuthorKey AuthorKey { get; } = authorKey;
}

public sealed class AuthorScopedCoordinateNavigationParameter(CoordinateCard card, AuthorKey authorKey)
{
    public CoordinateCard Card { get; } = card;
    public AuthorKey AuthorKey { get; } = authorKey;
}

public sealed class AuthorPostNavigationParameter(AuthorPost post, AuthorKey authorKey)
{
    public AuthorPost Post { get; } = post;
    public AuthorKey AuthorKey { get; } = authorKey;
}

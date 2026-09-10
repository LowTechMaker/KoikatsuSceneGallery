namespace KoikatsuSceneGallery.Models;

/// <summary>
/// What the user recorded about one character card.
/// </summary>
/// <remarks>
/// <see cref="Label"/> is a string rather than the enum so an unrecognised
/// value degrades this one entry instead of failing the whole document, and so
/// the pre-<see cref="Superseded"/> token still reads correctly.
/// <see cref="FileSize"/> is written but never read: it is there so a future
/// "reattach orphaned annotations" command has something to match on without
/// needing a schema bump.
/// </remarks>
internal sealed record CharacterAnnotationEntry
{
    public string? Label { get; init; }

    /// <summary>
    /// Whether a newer card has replaced this one. Independent of
    /// <see cref="Label"/> so a replaced what-if version can say both things.
    /// </summary>
    public bool Superseded { get; init; }

    public string? Note { get; init; }

    /// <summary>
    /// The character this card should be filed under, overriding its own name.
    /// This is how a renamed what-if version rejoins the original's versions.
    /// </summary>
    public string? GroupKey { get; init; }

    public long FileSize { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public CharacterVersionKind ParsedKind => CharacterVersionKinds.Parse(Label);

    /// <summary>
    /// Superseded, including documents written before this was its own field,
    /// where the label itself carried the meaning.
    /// </summary>
    public bool IsSuperseded => Superseded || CharacterVersionKinds.IsLegacySuperseded(Label);

    /// <summary>Whether this entry still says anything worth storing.</summary>
    public bool IsEmpty
        => ParsedKind == CharacterVersionKind.Current
           && !IsSuperseded
           && string.IsNullOrWhiteSpace(Note)
           && string.IsNullOrWhiteSpace(GroupKey);
}

/// <summary>
/// Every annotation for the cards in one directory.
/// </summary>
/// <remarks>
/// One document per directory rather than per card: the scan touches every
/// card, so a per-card file would mean one read per card. This shape lets the
/// store read once per directory and answer from memory after that.
/// </remarks>
internal sealed record CharacterAnnotationDocument(int SchemaVersion)
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Keyed by file name, not path, so the library stays portable.</summary>
    public IReadOnlyDictionary<string, CharacterAnnotationEntry> Cards { get; init; } =
        new Dictionary<string, CharacterAnnotationEntry>(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

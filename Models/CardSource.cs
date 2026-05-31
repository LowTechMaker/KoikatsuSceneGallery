namespace KoikatsuSceneGallery.Models;

/// <summary>
/// Unified "source / version" classification for a card, derived from its game
/// version and whether it carries Madevil repack data. Phase 2b uses this for
/// the character gallery filter; the scene gallery is intended to adopt the same
/// scheme in a later unification step.
/// </summary>
public enum CardSource
{
    Unknown,
    KoikatsuSunshine,
    /// <summary>Base Koikatsu (无印 / HF Patch), i.e. not a Madevil repack.</summary>
    KoikatsuHF,
    Madevil
}

public static class CardSourceClassifier
{
    /// <summary>
    /// Maps game version + Madevil flag to a single source category. Madevil
    /// takes precedence (it's a base-KK repack); otherwise the game version
    /// decides. Game-unknown cards are <see cref="CardSource.Unknown"/>.
    /// </summary>
    public static CardSource Classify(GameVersion game, bool isMadevil)
    {
        if (isMadevil) return CardSource.Madevil;
        return game switch
        {
            GameVersion.KoikatsuSunshine => CardSource.KoikatsuSunshine,
            GameVersion.Koikatsu => CardSource.KoikatsuHF,
            _ => CardSource.Unknown
        };
    }
}

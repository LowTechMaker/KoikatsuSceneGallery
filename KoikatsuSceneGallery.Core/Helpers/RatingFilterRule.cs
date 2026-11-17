namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Which ratings the app shows. One value for the whole app (held by the
/// settings, chosen in the title bar). The order matches the menus, which
/// bind by index.
/// </summary>
public enum RatingFilterOption
{
    All,
    GeneralOnly,
    /// <summary>R-18 including R-18G.</summary>
    R18AndR18G,
    /// <summary>R-18 excluding R-18G.</summary>
    R18Only,
    R18GOnly
}

/// <summary>
/// The one place that decides whether a scene card passes the rating filter:
/// the scene gallery, every scoped browse, random discovery and the home page
/// all ask this, with the card's resolved rating (<see cref="SceneRating"/>).
/// </summary>
internal static class RatingFilterRule
{
    /// <summary>
    /// The filter actually applied. With filtering switched off in the settings
    /// nothing is filtered; the choice itself is kept, so switching it back on
    /// brings it back.
    /// </summary>
    public static RatingFilterOption Effective(bool enabled, RatingFilterOption choice)
        => enabled ? choice : RatingFilterOption.All;

    public static bool Passes(RatingFilterOption filter, int rating) => filter switch
    {
        RatingFilterOption.GeneralOnly => rating == SceneRating.General,
        RatingFilterOption.R18AndR18G => rating >= SceneRating.R18,
        RatingFilterOption.R18Only => rating == SceneRating.R18,
        RatingFilterOption.R18GOnly => rating == SceneRating.R18G,
        _ => true,
    };

    /// <summary>The stored form: the option's name.</summary>
    public static string ToConfigValue(RatingFilterOption option) => option.ToString();

    /// <summary>A stored choice; anything missing or unknown is <see cref="RatingFilterOption.All"/>.</summary>
    public static RatingFilterOption Parse(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && Enum.TryParse<RatingFilterOption>(value.Trim(), ignoreCase: true, out var option)
           && Enum.IsDefined(option)
            ? option
            : RatingFilterOption.All;
}

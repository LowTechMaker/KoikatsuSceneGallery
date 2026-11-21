namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// The ratings the app shows: any non-empty combination of the three. One value
/// for the whole app (held by the settings, toggled in the title bar).
/// </summary>
[Flags]
public enum RatingSet
{
    General = 1,
    R18 = 2,
    R18G = 4,
    All = General | R18 | R18G,
}

/// <summary>
/// The one place that decides whether a scene card passes the rating filter:
/// the scene gallery, every scoped browse, random discovery and the home page
/// all ask this, with the card's resolved rating (<see cref="SceneRating"/>).
/// </summary>
internal static class RatingFilterRule
{
    /// <summary>The flag for a rating on the platform scale (0 = G, 1 = R-18, 2 = R-18G); 0 for anything else.</summary>
    public static RatingSet Of(int rating) => rating switch
    {
        SceneRating.General => RatingSet.General,
        SceneRating.R18 => RatingSet.R18,
        SceneRating.R18G => RatingSet.R18G,
        _ => 0,
    };

    /// <summary>A set that can apply: an empty one (never offered) shows everything rather than nothing.</summary>
    private static RatingSet Normalize(RatingSet set) => (set & RatingSet.All) == 0 ? RatingSet.All : set & RatingSet.All;

    /// <summary>
    /// The filter actually applied. With filtering switched off in the settings
    /// nothing is filtered; the set itself is kept, so switching it back on
    /// brings it back.
    /// </summary>
    public static RatingSet Effective(bool enabled, RatingSet set) => enabled ? Normalize(set) : RatingSet.All;

    /// <summary>
    /// Whether a card of <paramref name="rating"/> passes. A rating off the
    /// scale passes only while every rating is shown.
    /// </summary>
    public static bool Passes(RatingSet set, int rating)
    {
        set = Normalize(set);
        return set == RatingSet.All || (set & Of(rating)) != 0;
    }

    /// <summary>Whether <paramref name="rating"/> is the only one on: the badge that cannot be turned off.</summary>
    public static bool IsLast(RatingSet set, RatingSet rating) => Normalize(set) == rating;

    /// <summary>The set with <paramref name="rating"/> turned on or off; turning off the last one leaves the set as it is.</summary>
    public static RatingSet Toggle(RatingSet set, RatingSet rating)
    {
        set = Normalize(set);
        var toggled = set ^ rating;
        return (toggled & RatingSet.All) == 0 ? set : toggled;
    }

    private static readonly (RatingSet Flag, string Name)[] Names =
        [(RatingSet.General, "G"), (RatingSet.R18, "R18"), (RatingSet.R18G, "R18G")];

    /// <summary>
    /// The stored form: the ratings joined by "+" ("G+R18"). It shares no name
    /// with the five options stored before, which <see cref="Parse"/> still reads.
    /// </summary>
    public static string ToConfigValue(RatingSet set)
        => string.Join("+", Names.Where(n => (Normalize(set) & n.Flag) != 0).Select(n => n.Name));

    /// <summary>
    /// A stored value: the "+" form, or one of the five options of the earlier
    /// filter (All, GeneralOnly, R18AndR18G, R18Only, R18GOnly). Anything
    /// missing, unknown or empty is <see cref="RatingSet.All"/>.
    /// </summary>
    public static RatingSet Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return RatingSet.All;
        var text = value.Trim();
        var old = text.ToUpperInvariant() switch
        {
            "ALL" => RatingSet.All,
            "GENERALONLY" => RatingSet.General,
            "R18ANDR18G" => RatingSet.R18 | RatingSet.R18G,
            "R18ONLY" => RatingSet.R18,
            "R18GONLY" => RatingSet.R18G,
            _ => (RatingSet)0,
        };
        if (old != 0) return old;

        RatingSet set = 0;
        foreach (var part in text.Split('+'))
        {
            var name = part.Trim();
            var match = Names.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match.Name is null) return RatingSet.All;
            set |= match.Flag;
        }
        return Normalize(set);
    }
}

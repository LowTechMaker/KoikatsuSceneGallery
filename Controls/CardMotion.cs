using System.Numerics;
using Microsoft.UI.Composition;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The app's one set of motion constants.
/// </summary>
/// <remarks>
/// Extracted from <see cref="StagedCardFlightAnimator"/>, which owned them
/// first, so a second animation cannot quietly drift to a different tempo. The
/// values are unchanged.
/// </remarks>
internal static class CardMotion
{
    public static readonly TimeSpan FlyDuration = TimeSpan.FromMilliseconds(260);
    public static readonly TimeSpan ReturnDuration = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(18);

    /// <summary>
    /// Total time the staggered launches may span. Beyond this the per-item
    /// delay shrinks, so a full viewport does not take seconds to settle.
    /// </summary>
    public static readonly TimeSpan StaggerBudget = TimeSpan.FromMilliseconds(120);

    public static bool AnimationsEnabled
        => new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    public static CompositionEasingFunction Ease(Compositor compositor)
        => compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.2f, 1f));
}

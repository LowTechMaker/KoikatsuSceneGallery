using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The app's one place for motion: the tempo (durations, easing), whether
/// animation is on at all, and the few movements everything uses.
/// </summary>
/// <remarks>
/// <para>
/// Extracted first from <see cref="StagedCardFlightAnimator"/>, so a second
/// animation could not quietly drift to a different tempo. It became the
/// app's motion scheduler when the user found the library opening, a group
/// opening and the loupe each moving (or not moving) in their own way: every
/// animation now takes its timing and its on/off from here.
/// </para>
/// <list type="bullet">
/// <item><see cref="Entrance"/>: content arriving (a library tab, a page's grid) fades in and settles up.</item>
/// <item><see cref="DrillIn"/>: going into something (a group) grows the new content out of where it was opened.</item>
/// <item>The card flights (<see cref="StagedCardFlightAnimator"/>, the loupe, the group collapse) use
/// <see cref="FlyDuration"/> / <see cref="ReturnDuration"/> and <see cref="Decelerate"/>.</item>
/// </list>
/// </remarks>
internal static class CardMotion
{
    public static readonly TimeSpan FlyDuration = TimeSpan.FromMilliseconds(260);
    public static readonly TimeSpan ReturnDuration = TimeSpan.FromMilliseconds(180);
    /// <summary>Content arriving: shorter than a flight, it should not hold the eye.</summary>
    public static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(18);

    // ---- fades
    /// <summary>Something small appearing (a hint, a badge).</summary>
    public static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(150);
    /// <summary>Something small leaving on its own, unhurried (the loupe's key hints).</summary>
    public static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(600);
    /// <summary>One picture giving way to the next in the same place (stepping in the loupe).</summary>
    public static readonly TimeSpan CrossfadeDuration = TimeSpan.FromMilliseconds(140);

    // ---- a panel flying into the control that opened it (the import page's add-files card)
    public static readonly TimeSpan CollapseIntoDuration = TimeSpan.FromMilliseconds(300);

    // ---- ambient motion: loops and slow cycles that are not a response to a click
    /// <summary>An author tile turning to its next picture.</summary>
    public static readonly TimeSpan AmbientDuration = TimeSpan.FromMilliseconds(600);
    /// <summary>One breath of a loading placeholder.</summary>
    public static readonly TimeSpan PulseDuration = TimeSpan.FromMilliseconds(1400);

    // ---- the tag cloud (TagCloudMotion): its own, slower choreography, kept here with the rest
    public static readonly TimeSpan TagRevealDuration = TimeSpan.FromMilliseconds(460);
    public static readonly TimeSpan TagEnterStagger = TimeSpan.FromMilliseconds(26);
    public static readonly TimeSpan TagSwapOutDuration = TimeSpan.FromMilliseconds(160);
    public static readonly TimeSpan CloudArrivalDuration = TimeSpan.FromMilliseconds(700);
    public static readonly TimeSpan CloudArrivalBudget = TimeSpan.FromMilliseconds(700);
    public static readonly TimeSpan CloudSwapDuration = TimeSpan.FromMilliseconds(320);
    public static readonly TimeSpan CloudSwapStagger = TimeSpan.FromMilliseconds(10);
    public static readonly TimeSpan CloudSwapBudget = TimeSpan.FromMilliseconds(220);

    /// <summary>A XAML storyboard's duration from one of the tempos above.</summary>
    public static Microsoft.UI.Xaml.Duration Xaml(TimeSpan duration) => new(duration);

    /// <summary>XAML storyboard easings matching <see cref="Decelerate"/> (arriving) and an accelerating leave.</summary>
    public static Microsoft.UI.Xaml.Media.Animation.EasingFunctionBase XamlDecelerate()
        => new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };
    public static Microsoft.UI.Xaml.Media.Animation.EasingFunctionBase XamlAccelerate()
        => new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn };

    /// <summary>
    /// Total time the staggered launches may span. Beyond this the per-item
    /// delay shrinks, so a full viewport does not take seconds to settle.
    /// </summary>
    public static readonly TimeSpan StaggerBudget = TimeSpan.FromMilliseconds(120);

    /// <summary>Windows' own "show animations" setting; off means everything simply appears.</summary>
    public static bool AnimationsEnabled
        => new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    /// <summary>Standard: accelerates out, settles in. For things moving between two places.</summary>
    public static CompositionEasingFunction Ease(Compositor compositor)
        => compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.2f, 1f));

    /// <summary>Decelerate: fast start, soft landing. For things arriving.</summary>
    public static CompositionEasingFunction Decelerate(Compositor compositor)
        => compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

    /// <summary>
    /// Content arriving: fades in while settling up from <paramref name="rise"/> pixels below.
    /// </summary>
    public static void Entrance(UIElement element, float rise = 16)
    {
        if (!AnimationsEnabled) return;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var ease = Decelerate(compositor);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = EnterDuration;
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(0, new Vector3(0, rise, 0));
        move.InsertKeyFrame(1, Vector3.Zero, ease);
        move.Duration = EnterDuration;

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", move);
    }

    /// <summary>
    /// Leaving what was drilled into: the content shrinks back towards
    /// <paramref name="origin"/> (where it was opened from) and fades, then
    /// <paramref name="done"/> runs, which puts up what it returns to. Without
    /// animation <paramref name="done"/> runs at once.
    /// </summary>
    public static void DrillOut(UIElement element, Vector2 origin, Action done)
    {
        if (!AnimationsEnabled)
        {
            done();
            return;
        }
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var ease = Ease(compositor);
        visual.CenterPoint = new Vector3(origin, 0);

        var shrink = compositor.CreateVector3KeyFrameAnimation();
        shrink.InsertKeyFrame(1, new Vector3(0.85f, 0.85f, 1), ease);
        shrink.Duration = ReturnDuration;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0, ease);
        fade.Duration = ReturnDuration;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Scale", shrink);
        visual.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) => done();
    }

    /// <summary>
    /// Arriving back where a drill-in started: the content settles in from a
    /// little larger, around <paramref name="origin"/>, as if the camera pulled back.
    /// </summary>
    public static void PullBack(UIElement element, Vector2 origin)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!AnimationsEnabled)
        {
            visual.Scale = Vector3.One;
            visual.Opacity = 1;
            return;
        }
        var compositor = visual.Compositor;
        var ease = Decelerate(compositor);
        visual.CenterPoint = new Vector3(origin, 0);

        var settle = compositor.CreateVector3KeyFrameAnimation();
        settle.InsertKeyFrame(0, new Vector3(1.08f, 1.08f, 1));
        settle.InsertKeyFrame(1, Vector3.One, ease);
        settle.Duration = FlyDuration;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = FlyDuration;

        visual.StartAnimation("Scale", settle);
        visual.StartAnimation("Opacity", fade);
    }

    /// <summary>Stops any of these movements on <paramref name="element"/> and puts it back as laid out.</summary>
    /// <summary>
    /// A card dealt into the holder (random discovery's hand): it drops in from
    /// <paramref name="drop"/> pixels above and fades in, after
    /// <paramref name="delay"/> so a hand arrives card by card. From above,
    /// as cards are put into a rack (user request; it used to rise from below).
    /// </summary>
    public static void Deal(UIElement element, float drop, TimeSpan delay)
    {
        if (!AnimationsEnabled) return;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var ease = Decelerate(compositor);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = FlyDuration;
        fade.DelayTime = delay;
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(0, new Vector3(0, -drop, 0));
        move.InsertKeyFrame(1, Vector3.Zero, ease);
        move.Duration = FlyDuration;
        move.DelayTime = delay;
        move.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", move);
    }

    /// <summary>The pause between two cards of a dealt hand.</summary>
    public static readonly TimeSpan DealStagger = TimeSpan.FromMilliseconds(35);

    /// <summary>
    /// A card drawn up out of the holder under the pointer (<paramref name="up"/>
    /// true), or settling back into its slot. Goes from wherever it is, so a
    /// quick pass over several cards never jumps.
    /// </summary>
    public static void Lift(UIElement element, bool up, float rise)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var target = up ? new Vector3(0, -rise, 0) : Vector3.Zero;
        if (!AnimationsEnabled)
        {
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", target);
            return;
        }
        var compositor = visual.Compositor;
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(1, target, Ease(compositor));
        move.Duration = up ? ReturnDuration : FlyDuration;
        visual.StartAnimation("Translation", move);
    }

    public static void Reset(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Scale");
        visual.StopAnimation("Opacity");
        visual.Scale = Vector3.One;
        visual.Opacity = 1;
    }

    /// <summary>One picture giving way to the next in the same place: a quick fade up from dim.</summary>
    public static void Crossfade(UIElement element)
    {
        if (!AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0.35f);
        fade.InsertKeyFrame(1, 1, Decelerate(visual.Compositor));
        fade.Duration = CrossfadeDuration;
        visual.StartAnimation("Opacity", fade);
    }

    /// <summary>
    /// Going into something: the new content grows out of <paramref name="origin"/>
    /// (in the element's own coordinates, e.g. the centre of the tile that was opened)
    /// while it fades in.
    /// </summary>
    public static void DrillIn(UIElement element, Vector2 origin)
    {
        if (!AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var ease = Decelerate(compositor);
        visual.CenterPoint = new Vector3(origin, 0);

        var grow = compositor.CreateVector3KeyFrameAnimation();
        grow.InsertKeyFrame(0, new Vector3(0.85f, 0.85f, 1));
        grow.InsertKeyFrame(1, Vector3.One, ease);
        grow.Duration = FlyDuration;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = FlyDuration;

        visual.StartAnimation("Scale", grow);
        visual.StartAnimation("Opacity", fade);
    }
}

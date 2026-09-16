using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// Flies the staged thumbnails into the folder when the user starts dragging
/// it, and puts them back if the drag is abandoned.
/// </summary>
/// <remarks>
/// Hand-rolled on the Composition API, like
/// <see cref="AuthorLiveTileControl"/> — the only other animation in the app —
/// and sharing its easing so the two read as one motion language. The number
/// of visuals is bounded by the strip's thumbnail cap, so there is no
/// batch-size case to special-case.
///
/// The single place that honours the system's "animations off" setting: with
/// it off the strip simply collapses, which conveys the same thing without
/// motion and leaves drag, drop and import behaving identically.
/// </remarks>
internal sealed class StagedCardFlightAnimator
{
    private static readonly TimeSpan FlyDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan ReturnDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(18);

    private readonly FrameworkElement _strip;
    private readonly FrameworkElement _folder;
    private readonly List<FrameworkElement> _inFlight = [];

    private CompositionEasingFunction? _easing;

    public StagedCardFlightAnimator(FrameworkElement strip, FrameworkElement folder)
    {
        _strip = strip;
        _folder = folder;
    }

    private static bool AnimationsEnabled
        => new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    /// <summary>Sends every visible thumbnail into the folder.</summary>
    public void FlyIn(IReadOnlyList<FrameworkElement> thumbnails)
    {
        Reset();

        if (!AnimationsEnabled)
        {
            _strip.Opacity = 0;
            return;
        }

        var compositor = ElementCompositionPreview.GetElementVisual(_folder).Compositor;
        _easing ??= compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.4f, 0f),
            new Vector2(0.2f, 1f));

        for (var i = 0; i < thumbnails.Count; i++)
        {
            var thumbnail = thumbnails[i];
            if (!TryGetTranslationToFolder(thumbnail, out var translation))
                continue;

            // Translation, not Offset: it composes with the layout position
            // instead of replacing it, so nothing has to be restored by hand.
            ElementCompositionPreview.SetIsTranslationEnabled(thumbnail, true);
            var visual = ElementCompositionPreview.GetElementVisual(thumbnail);
            var delay = Stagger * i;

            var move = compositor.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(1f, translation, _easing);
            move.Duration = FlyDuration;
            move.DelayTime = delay;

            var shrink = compositor.CreateVector3KeyFrameAnimation();
            shrink.InsertKeyFrame(1f, new Vector3(0.15f, 0.15f, 1f), _easing);
            shrink.Duration = FlyDuration;
            shrink.DelayTime = delay;

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 0f, _easing);
            fade.Duration = FlyDuration;
            fade.DelayTime = delay;

            visual.CenterPoint = new Vector3(
                (float)(thumbnail.ActualWidth / 2),
                (float)(thumbnail.ActualHeight / 2),
                0);
            visual.StartAnimation("Translation", move);
            visual.StartAnimation("Scale", shrink);
            visual.StartAnimation("Opacity", fade);
            _inFlight.Add(thumbnail);
        }
    }

    /// <summary>Brings the thumbnails back after an abandoned drag.</summary>
    public void FlyBack()
    {
        if (!AnimationsEnabled)
        {
            _strip.Opacity = 1;
            return;
        }

        if (_inFlight.Count == 0)
            return;

        var compositor = ElementCompositionPreview.GetElementVisual(_folder).Compositor;

        foreach (var thumbnail in _inFlight)
        {
            var visual = ElementCompositionPreview.GetElementVisual(thumbnail);
            StopAnimations(visual);

            var move = compositor.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(1f, Vector3.Zero, _easing);
            move.Duration = ReturnDuration;

            var grow = compositor.CreateVector3KeyFrameAnimation();
            grow.InsertKeyFrame(1f, Vector3.One, _easing);
            grow.Duration = ReturnDuration;

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 1f, _easing);
            fade.Duration = ReturnDuration;

            visual.StartAnimation("Translation", move);
            visual.StartAnimation("Scale", grow);
            visual.StartAnimation("Opacity", fade);
        }

        _inFlight.Clear();
    }

    /// <summary>
    /// Clears any half-finished animation. Needed when the page is revisited:
    /// it is cached, so animated state outlives a navigation.
    /// </summary>
    public void Reset()
    {
        _strip.Opacity = 1;

        foreach (var thumbnail in _inFlight)
        {
            var visual = ElementCompositionPreview.GetElementVisual(thumbnail);
            StopAnimations(visual);
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            visual.Scale = Vector3.One;
            visual.Opacity = 1;
        }

        _inFlight.Clear();
    }

    private static void StopAnimations(Visual visual)
    {
        visual.StopAnimation("Translation");
        visual.StopAnimation("Scale");
        visual.StopAnimation("Opacity");
    }

    /// <summary>
    /// How far this thumbnail has to move for its centre to land on the
    /// folder's, expressed in the thumbnail's own coordinate space.
    /// </summary>
    private bool TryGetTranslationToFolder(FrameworkElement thumbnail, out Vector3 translation)
    {
        translation = Vector3.Zero;

        if (thumbnail.ActualWidth <= 0 || _folder.ActualWidth <= 0)
            return false;

        try
        {
            var origin = thumbnail.TransformToVisual(_folder).TransformPoint(new Point(0, 0));
            translation = new Vector3(
                (float)(_folder.ActualWidth / 2 - (origin.X + thumbnail.ActualWidth / 2)),
                (float)(_folder.ActualHeight / 2 - (origin.Y + thumbnail.ActualHeight / 2)),
                0);
            return true;
        }
        catch (ArgumentException)
        {
            // An element that is not in the visual tree has no transform.
            return false;
        }
    }
}

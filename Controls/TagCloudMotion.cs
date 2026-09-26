using System.Numerics;
using KoikatsuSceneGallery.Helpers;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The tag cloud's motion: a slow drift while it sits there, and a zoom into
/// the tag you picked.
/// </summary>
/// <remarks>
/// The drift runs on the compositor thread, so the cost is per animated visual
/// rather than per frame of UI work — but the cloud panel does not virtualize,
/// so the tag cap in <see cref="TagCloudAggregator"/> is what keeps the count
/// bounded. Everything here is a no-op when the system has animations off.
/// </remarks>
internal sealed class TagCloudMotion
{
    /// <summary>
    /// Deliberately small: the spiral packs words close together, so a large
    /// drift would let neighbours overlap. The parallax below is what carries
    /// the sense of depth, not the distance travelled.
    /// </summary>
    private const float DriftBase = 3.5f;

    /// <summary>How far the chosen tag flies past the camera.</summary>
    private const float RevealScale = 14f;

    /// <summary>How far the rest of the cloud falls back as the camera moves in.</summary>
    private const float RecedeScale = 0.45f;

    private static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(460);
    private static readonly TimeSpan EnterStagger = TimeSpan.FromMilliseconds(26);

    /// <summary>Quick recede for the cloud being replaced when platforms switch.</summary>
    private static readonly TimeSpan SwapOutDuration = TimeSpan.FromMilliseconds(160);
    private const float SwapOutScale = 0.88f;

    /// <summary>The scale a tag arrives from, as if far behind the screen.</summary>
    private const float EnterScale = 0.35f;

    /// <summary>
    /// A tag's size, falling back to what it measured to when it has not been
    /// arranged yet.
    /// </summary>
    /// <remarks>
    /// Guarding on <c>ActualWidth</c> alone silently skipped every tag: on a
    /// Canvas the children are measured by the caller but only arranged on a
    /// later layout pass, so both the entry and the drift became no-ops and the
    /// cloud simply appeared, frozen. Falling back to DesiredSize means a
    /// mistimed call degrades to a slightly wrong centre point rather than to
    /// no animation at all.
    /// </remarks>
    private static (float Width, float Height) Extent(FrameworkElement element)
    {
        var width = element.ActualWidth > 0 ? element.ActualWidth : element.DesiredSize.Width;
        var height = element.ActualHeight > 0 ? element.ActualHeight : element.DesiredSize.Height;
        return ((float)width, (float)height);
    }

    private readonly Random _random = new();
    // Track finite transitions as well as idle loops so navigation can cancel
    // either without leaving a cached cloud half transparent or enlarged.
    private readonly HashSet<FrameworkElement> _animated = [];
    private int _generation;

    /// <summary>
    /// Records an element as animating, and makes "Translation" exist on its
    /// visual first.
    /// </summary>
    /// <remarks>
    /// "Translation" is not a real <c>Visual</c> property — it only appears in
    /// the property set once <see cref="ElementCompositionPreview.SetIsTranslationEnabled"/>
    /// has run for that element. <see cref="StopIdle"/> stops all three
    /// properties on everything it tracks, so an element that entered the set
    /// through a transition that never translated it made StopIdle throw
    /// ArgumentException — which aborted it half way and left the rest of the
    /// cloud stuck mid-transition. Enabling it for every tracked element is
    /// idempotent and keeps that stop uniform.
    /// </remarks>
    private void Track(FrameworkElement element)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        _animated.Add(element);
    }

    /// <summary>
    /// Accelerating, unlike the app's usual decelerating curve. Something
    /// rushing past the camera speeds up as it approaches; an ease-out makes it
    /// look like it is braking in the viewer's face instead.
    /// </summary>
    private static CompositionEasingFunction Accelerate(Compositor compositor)
        => compositor.CreateCubicBezierEasingFunction(new Vector2(0.55f, 0f), new Vector2(1f, 0.45f));

    /// <summary>
    /// How briskly a cloud arrives. Landing on the page earns the full,
    /// unhurried sequence; flicking between platforms does not — there the same
    /// motion at the same pace just reads as waiting.
    /// </summary>
    internal readonly record struct CloudEntryPace(TimeSpan Duration, TimeSpan PerItem, TimeSpan Budget)
    {
        public static readonly CloudEntryPace Arrival =
            new(TimeSpan.FromMilliseconds(700), EnterStagger, TimeSpan.FromMilliseconds(700));

        public static readonly CloudEntryPace Swap =
            new(TimeSpan.FromMilliseconds(320), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(220));
    }

    /// <summary>
    /// Fades the outgoing cloud away, a short step back rather than the deep
    /// recede of a reveal — nothing is being zoomed into, the set is just
    /// being replaced.
    /// </summary>
    public void SwapOut(IReadOnlyList<Control> tags, Action onDone)
    {
        StopIdle(resetVisuals: false);
        if (!CardMotion.AnimationsEnabled || tags.Count == 0) { onDone(); return; }

        var generation = ++_generation;
        var compositor = ElementCompositionPreview.GetElementVisual(tags[0]).Compositor;
        var easing = CardMotion.Ease(compositor);
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        foreach (var tag in tags)
        {
            var (width, height) = Extent(tag);
            if (width <= 0 || height <= 0) continue;
            var visual = ElementCompositionPreview.GetElementVisual(tag);
            Track(tag);
            visual.CenterPoint = new(width / 2, height / 2, 0);

            var shrink = compositor.CreateVector3KeyFrameAnimation();
            shrink.InsertKeyFrame(1f, new(SwapOutScale, SwapOutScale, 1f), easing);
            shrink.Duration = SwapOutDuration;

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 0f, easing);
            fade.Duration = SwapOutDuration;

            visual.StartAnimation("Scale", shrink);
            visual.StartAnimation("Opacity", fade);
        }

        batch.Completed += (_, _) =>
        {
            if (generation == _generation) onDone();
        };
        batch.End();
    }

    /// <summary>
    /// Brings the cloud in from depth: every tag starts small and faint, as if
    /// far behind the screen, and settles forward in a staggered wave. This is
    /// the part that reads as the PS2 boot sequence rather than a grid of text
    /// simply appearing.
    /// </summary>
    public void EnterFromDepth(IReadOnlyList<Control> tags, Action onSettled, CloudEntryPace pace)
    {
        StopIdle();
        if (!CardMotion.AnimationsEnabled || tags.Count == 0) { onSettled(); return; }

        var generation = ++_generation;
        var batchCompositor = ElementCompositionPreview.GetElementVisual(tags[0]).Compositor;
        var batch = batchCompositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        // Budgeted, so a long cloud does not keep launching words long after the
        // first ones have landed.
        var stagger = CollapseFlightGeometry.StaggerFor(tags.Count, pace.PerItem, pace.Budget);
        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            var (width, height) = Extent(tag);
            if (width <= 0 || height <= 0) continue;
            var compositor = ElementCompositionPreview.GetElementVisual(tag).Compositor;
            var easing = CardMotion.Ease(compositor);
            var visual = ElementCompositionPreview.GetElementVisual(tag);
            Track(tag);
            visual.CenterPoint = new(width / 2, height / 2, 0);
            var delay = stagger * i;

            var forward = compositor.CreateVector3KeyFrameAnimation();
            forward.InsertKeyFrame(0f, new(EnterScale, EnterScale, 1f));
            forward.InsertKeyFrame(1f, Vector3.One, easing);
            forward.Duration = pace.Duration;
            forward.DelayTime = delay;
            forward.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            var appear = compositor.CreateScalarKeyFrameAnimation();
            appear.InsertKeyFrame(0f, 0f);
            appear.InsertKeyFrame(1f, 1f, easing);
            appear.Duration = pace.Duration;
            appear.DelayTime = delay;
            appear.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            visual.StartAnimation("Scale", forward);
            visual.StartAnimation("Opacity", appear);
        }
        // The idle drift animates the same properties, so it can only take over
        // once the arrival has finished.
        batch.Completed += (_, _) =>
        {
            if (generation == _generation) onSettled();
        };
        batch.End();
    }

    /// <summary>Starts the drift from the settled transition's current values.</summary>
    public void StartIdle(IEnumerable<Control> tags)
    {
        StopIdle(resetVisuals: !CardMotion.AnimationsEnabled);
        if (!CardMotion.AnimationsEnabled) return;

        foreach (var tag in tags)
        {
            var (width, height) = Extent(tag);
            if (width <= 0 || height <= 0) continue;
            var compositor = ElementCompositionPreview.GetElementVisual(tag).Compositor;
            var easing = CardMotion.Ease(compositor);
            ElementCompositionPreview.SetIsTranslationEnabled(tag, true);
            var visual = ElementCompositionPreview.GetElementVisual(tag);
            visual.CenterPoint = new(width / 2, height / 2, 0);

            // Bigger type drifts further: the parallax is what reads as depth
            // rather than as jitter.
            var reach = DriftBase * (float)Math.Max(1, tag.FontSize / TagCloudAggregator.MinFontSize);
            var drift = compositor.CreateVector3KeyFrameAnimation();
            drift.InsertKeyFrame(0.5f, new(Signed(reach), Signed(reach), 0), easing);
            drift.InsertKeyFrame(1f, Vector3.Zero, easing);
            Loop(drift, 4000, 3000);
            visual.StartAnimation("Translation", drift);

            // 0.85, not 0.72: a deeper swing reads as blinking rather than as
            // the slight movement that was asked for.
            var breathe = compositor.CreateScalarKeyFrameAnimation();
            breathe.InsertKeyFrame(0.5f, 0.85f, easing);
            breathe.InsertKeyFrame(1f, 1f, easing);
            Loop(breathe, 3500, 4500);
            visual.StartAnimation("Opacity", breathe);

            var pulse = compositor.CreateVector3KeyFrameAnimation();
            pulse.InsertKeyFrame(0.5f, new(1.03f, 1.03f, 1f), easing);
            pulse.InsertKeyFrame(1f, Vector3.One, easing);
            Loop(pulse, 3800, 4200);
            visual.StartAnimation("Scale", pulse);

            Track(tag);
        }
    }

    /// <summary>
    /// Cancels the current motion. Navigation resets the cloud; a transition
    /// keeps the compositor's current values so its next animation can take
    /// over without snapping the drifting words back to their layout positions.
    /// </summary>
    public void StopIdle(bool resetVisuals = true)
    {
        _generation++;
        foreach (var tag in _animated)
        {
            var visual = ElementCompositionPreview.GetElementVisual(tag);
            visual.StopAnimation("Translation");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Opacity");
            if (resetVisuals)
            {
                visual.Properties.InsertVector3("Translation", Vector3.Zero);
                visual.Scale = Vector3.One;
                visual.Opacity = 1;
            }
        }
        // Always cleared, whatever resetVisuals says: the set means "currently
        // animating", and every caller that passes false re-adds what it then
        // animates. Keeping entries across a cloud rebuild accumulated detached
        // elements whose visuals no longer carry Translation at all.
        _animated.Clear();
    }

    /// <summary>
    /// Zooms <paramref name="chosen"/> towards the viewer while the rest of the
    /// cloud fades, then calls <paramref name="onRevealed"/>.
    /// </summary>
    public void Reveal(FrameworkElement chosen, IEnumerable<FrameworkElement> others, Action onRevealed)
    {
        StopIdle(resetVisuals: !CardMotion.AnimationsEnabled);
        if (!CardMotion.AnimationsEnabled) { onRevealed(); return; }

        var generation = ++_generation;
        var compositor = ElementCompositionPreview.GetElementVisual(chosen).Compositor;
        var easing = CardMotion.Ease(compositor);
        var accelerate = Accelerate(compositor);

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        // The rest of the cloud falls away rather than merely fading: the
        // parallax between it and the chosen tag is what sells a camera moving
        // forward instead of a cross-fade.
        foreach (var other in others)
        {
            Track(other);
            var otherVisual = ElementCompositionPreview.GetElementVisual(other);
            var (otherWidth, otherHeight) = Extent(other);
            otherVisual.CenterPoint = new(otherWidth / 2, otherHeight / 2, 0);

            var recede = compositor.CreateVector3KeyFrameAnimation();
            recede.InsertKeyFrame(1f, new(RecedeScale, RecedeScale, 1f), easing);
            recede.Duration = RevealDuration;
            var dim = compositor.CreateScalarKeyFrameAnimation();
            dim.InsertKeyFrame(1f, 0f, easing);
            dim.Duration = RevealDuration;

            otherVisual.StartAnimation("Scale", recede);
            otherVisual.StartAnimation("Opacity", dim);
        }

        var visual = ElementCompositionPreview.GetElementVisual(chosen);
        Track(chosen);
        var (chosenWidth, chosenHeight) = Extent(chosen);
        visual.CenterPoint = new(chosenWidth / 2, chosenHeight / 2, 0);
        var zoom = compositor.CreateVector3KeyFrameAnimation();
        zoom.InsertKeyFrame(1f, new(RevealScale, RevealScale, 1f), accelerate);
        zoom.Duration = RevealDuration;
        // Held opaque through most of the flight, so the tag is still readable
        // while it grows and only blows out at the very end.
        var vanish = compositor.CreateScalarKeyFrameAnimation();
        vanish.InsertKeyFrame(0.65f, 1f);
        vanish.InsertKeyFrame(1f, 0f, accelerate);
        vanish.Duration = RevealDuration;
        visual.StartAnimation("Scale", zoom);
        visual.StartAnimation("Opacity", vanish);
        batch.Completed += (_, _) =>
        {
            if (generation == _generation) onRevealed();
        };
        batch.End();
    }

    /// <summary>
    /// The camera pulling back out: the word that flew past returns from in
    /// front, the rest of the cloud comes forward from the distance, and both
    /// fade back in.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="Reveal"/>, and deliberately not a rebuild. The
    /// words are still there — just pushed away — so animating them home keeps
    /// the two directions continuous. Rebuilding and replaying the arrival
    /// instead made going back feel like landing on a different page.
    /// </remarks>
    public void ReturnToCloud(FrameworkElement? chosen, IEnumerable<Control> others, Action onSettled)
    {
        StopIdle(resetVisuals: !CardMotion.AnimationsEnabled);
        if (!CardMotion.AnimationsEnabled) { onSettled(); return; }

        var all = others.ToArray();
        var anchor = chosen ?? all.FirstOrDefault();
        if (anchor is null) { onSettled(); return; }

        var generation = ++_generation;
        var compositor = ElementCompositionPreview.GetElementVisual(anchor).Compositor;
        var easing = CardMotion.Ease(compositor);
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        var animated = 0;
        void Home(FrameworkElement element, TimeSpan delay)
        {
            var (width, height) = Extent(element);
            if (width <= 0 || height <= 0) return;
            animated++;
            var visual = ElementCompositionPreview.GetElementVisual(element);
            Track(element);
            visual.CenterPoint = new(width / 2, height / 2, 0);

            // Reveal retained the drifting position. Bring that position home
            // with the scale, rather than resetting it when idle resumes.
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            var translate = compositor.CreateVector3KeyFrameAnimation();
            translate.InsertKeyFrame(1f, Vector3.Zero, easing);
            translate.Duration = RevealDuration;
            translate.DelayTime = delay;

            var settle = compositor.CreateVector3KeyFrameAnimation();
            settle.InsertKeyFrame(1f, Vector3.One, easing);
            settle.Duration = RevealDuration;
            settle.DelayTime = delay;

            var reappear = compositor.CreateScalarKeyFrameAnimation();
            reappear.InsertKeyFrame(1f, 1f, easing);
            reappear.Duration = RevealDuration;
            reappear.DelayTime = delay;

            visual.StartAnimation("Translation", translate);
            visual.StartAnimation("Scale", settle);
            visual.StartAnimation("Opacity", reappear);
        }

        // The chosen word leads, since it is the one the eye followed out.
        if (chosen is not null) Home(chosen, TimeSpan.Zero);
        var stagger = CollapseFlightGeometry.StaggerFor(all.Length, EnterStagger / 2, CardMotion.StaggerBudget);
        for (var i = 0; i < all.Length; i++)
            if (!ReferenceEquals(all[i], chosen))
                Home(all[i], stagger * i);

        if (animated == 0)
        {
            // Nothing had a size to animate home. That happens when the page
            // was navigated away from while the results were up: the cloud host
            // is collapsed, so the words were never measured after the page
            // returned to the visual tree and still report zero.
            //
            // Reveal left them at opacity zero, and animating nothing would
            // leave them there — an empty cloud that never settles, and a
            // caller still waiting to be told the transition is over. Snap them
            // back instead and finish. The visuals are reset here rather than
            // through StopIdle because these words are not in the tracked set
            // after a navigation cleared it.
            batch.End();
            ResetVisuals(all);
            if (chosen is not null) ResetVisuals([chosen]);
            onSettled();
            return;
        }

        batch.Completed += (_, _) =>
        {
            if (generation == _generation) onSettled();
        };
        batch.End();
    }

    /// <summary>Returns elements to their settled position, size and opacity.</summary>
    private static void ResetVisuals(IEnumerable<FrameworkElement> elements)
    {
        foreach (var element in elements)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Translation");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Opacity");
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            visual.Scale = Vector3.One;
            visual.Opacity = 1;
        }
    }

    /// <summary>Sends a host away into the distance, the reverse of <see cref="PopIn"/>.</summary>
    public static void PopOut(FrameworkElement host, Point center, Action onDone, Func<bool>? isCurrent = null)
    {
        if (!CardMotion.AnimationsEnabled) { onDone(); return; }

        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        var easing = CardMotion.Ease(compositor);
        var visual = ElementCompositionPreview.GetElementVisual(host);
        visual.CenterPoint = new((float)center.X, (float)center.Y, 0);

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var shrink = compositor.CreateVector3KeyFrameAnimation();
        shrink.InsertKeyFrame(1f, new(0.8f, 0.8f, 1f), easing);
        shrink.Duration = RevealDuration;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 0f, easing);
        fade.Duration = RevealDuration;

        visual.StartAnimation("Scale", shrink);
        visual.StartAnimation("Opacity", fade);
        batch.Completed += (_, _) =>
        {
            if (isCurrent is not null && !isCurrent()) return;
            // Back to identity so the next reveal starts from a clean host.
            visual.StopAnimation("Scale");
            visual.StopAnimation("Opacity");
            visual.Scale = Vector3.One;
            visual.Opacity = 1;
            onDone();
        };
        batch.End();
    }

    /// <summary>Brings a host in from behind, centred on where the tag was.</summary>
    public static void PopIn(FrameworkElement host, Point center)
    {
        var visual = ElementCompositionPreview.GetElementVisual(host);
        if (!CardMotion.AnimationsEnabled)
        {
            // A canceled exit can retain its endpoint while the page is away.
            // Reduced motion still has to restore the result host's geometry.
            visual.StopAnimation("Scale");
            visual.StopAnimation("Opacity");
            visual.Scale = Vector3.One;
            visual.Opacity = 1;
            host.Opacity = 1;
            return;
        }

        var compositor = visual.Compositor;
        var easing = CardMotion.Ease(compositor);
        visual.CenterPoint = new((float)center.X, (float)center.Y, 0);

        var grow = compositor.CreateVector3KeyFrameAnimation();
        grow.InsertKeyFrame(0f, new(0.8f, 0.8f, 1f));
        grow.InsertKeyFrame(1f, Vector3.One, easing);
        grow.Duration = CardMotion.FlyDuration;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, easing);
        fade.Duration = CardMotion.FlyDuration;

        host.Opacity = 1;
        visual.StartAnimation("Scale", grow);
        visual.StartAnimation("Opacity", fade);
    }

    /// <summary>
    /// Staggers the freshly realized cards in, so the grid arrives as a wave
    /// rather than all at once.
    /// </summary>
    public static void PopInVisibleContainers(GridView grid)
    {
        if (!CardMotion.AnimationsEnabled) return;
        if (grid.ItemsPanelRoot is not ItemsWrapGrid panel) return;

        var first = Math.Max(0, panel.FirstVisibleIndex);
        var last = panel.LastVisibleIndex;
        var count = Math.Max(1, last - first + 1);
        var stagger = CollapseFlightGeometry.StaggerFor(count, CardMotion.Stagger, CardMotion.StaggerBudget);

        for (var i = first; i <= last; i++)
        {
            if (grid.ContainerFromIndex(i) is not FrameworkElement container) continue;
            if (container.ActualWidth <= 0) continue;

            var compositor = ElementCompositionPreview.GetElementVisual(container).Compositor;
            var easing = CardMotion.Ease(compositor);
            var visual = ElementCompositionPreview.GetElementVisual(container);
            visual.CenterPoint = new(
                (float)(container.ActualWidth / 2), (float)(container.ActualHeight / 2), 0);

            var delay = stagger * (i - first);
            var grow = compositor.CreateVector3KeyFrameAnimation();
            grow.InsertKeyFrame(0f, new(0.8f, 0.8f, 1f));
            grow.InsertKeyFrame(1f, Vector3.One, easing);
            grow.Duration = CardMotion.FlyDuration;
            grow.DelayTime = delay;
            grow.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, easing);
            fade.Duration = CardMotion.FlyDuration;
            fade.DelayTime = delay;
            fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            visual.StartAnimation("Scale", grow);
            visual.StartAnimation("Opacity", fade);
        }
    }

    private void Loop(KeyFrameAnimation animation, int baseMs, int jitterMs)
    {
        animation.Duration = TimeSpan.FromMilliseconds(baseMs + _random.Next(jitterMs));
        animation.DelayTime = TimeSpan.FromMilliseconds(_random.Next(1500));
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
    }

    private float Signed(float reach) => (float)((_random.NextDouble() * 2 - 1) * reach);
}

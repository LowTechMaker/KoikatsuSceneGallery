using System.Numerics;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace KoikatsuSceneGallery.Controls;

/// <summary>A member tile's thumbnail and where it sat when the group closed.</summary>
internal sealed record CapturedTile(Uri? ThumbnailUri, FlightRect Bounds);

/// <summary>
/// Flies a group's member tiles back onto the stacked tile they collapse into.
/// </summary>
/// <remarks>
/// Works on throwaway copies drawn on an overlay canvas rather than on the real
/// grid containers, for three reasons the real containers cannot satisfy:
///
/// The destination does not exist until after the rebuild — where the stacked
/// tile lands depends on how many entries precede it once the members fold into
/// one — so the geometry has to be captured before and measured after.
///
/// A refresh is not under the caller's control: the browser re-runs it on a
/// debounce for almost any view-model change, and a mid-flight rebuild would
/// leave a recycled container carrying a stale transform onto an unrelated
/// card.
///
/// Restoring the scroll position moves real containers underneath any animation
/// on them. The overlay is scroll-independent.
///
/// The motion is linear on all three properties, unlike the rest of the app: a
/// linear translation paired with an eased scale reads as a wobble.
/// </remarks>
internal sealed class GroupCollapseAnimator(Canvas overlay)
{
    /// <summary>
    /// Enough to cover a viewport; anything past it was not visible anyway, and
    /// the cost is one snapshot element each.
    /// </summary>
    private const int MaxFlightTiles = 24;

    private const float LandedScale = 0.2f;

    private int _generation;

    /// <summary>
    /// Where the realized tiles are right now, in the overlay's coordinate
    /// space. Must be called before the grid is rebuilt.
    /// </summary>
    public IReadOnlyList<CapturedTile> Capture(GridView grid)
    {
        if (!CardMotion.AnimationsEnabled) return [];
        if (grid.ItemsPanelRoot is not ItemsWrapGrid panel) return [];

        var first = Math.Max(0, panel.FirstVisibleIndex);
        var last = Math.Min(panel.LastVisibleIndex, first + MaxFlightTiles - 1);
        var captured = new List<CapturedTile>();
        for (var i = first; i <= last; i++)
        {
            // Virtualized-away tiles have no container, which is fine: they were
            // off screen, so there is nothing for the user to see move.
            if (grid.ContainerFromIndex(i) is not FrameworkElement container) continue;
            if (container.ActualWidth <= 0 || container.ActualHeight <= 0) continue;
            if (!TryBounds(container, out var bounds)) continue;

            var entry = grid.ItemFromContainer(container) as GalleryEntry;
            captured.Add(new(entry?.Card.ThumbnailUri, bounds));
        }
        return captured;
    }

    /// <summary>Sends the captured tiles onto <paramref name="destination"/>.</summary>
    public void Play(IReadOnlyList<CapturedTile> captured, FrameworkElement destination)
    {
        Finish();
        if (captured.Count == 0 || !CardMotion.AnimationsEnabled) return;
        if (!TryBounds(destination, out var target) || target.Width <= 0 || target.Height <= 0) return;

        var generation = ++_generation;
        var compositor = ElementCompositionPreview.GetElementVisual(overlay).Compositor;
        var linear = compositor.CreateLinearEasingFunction();
        var stagger = CollapseFlightGeometry.StaggerFor(
            captured.Count, CardMotion.Stagger, CardMotion.StaggerBudget);
        var order = CollapseFlightGeometry.OrderByDistance([.. captured.Select(t => t.Bounds)], target);

        overlay.Visibility = Visibility.Visible;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var flights = new List<(FrameworkElement Element, CapturedTile Tile, int Order)>(order.Length);
        for (var k = 0; k < order.Length; k++)
        {
            var tile = captured[order[k]];
            var element = CreateSnapshot(tile);
            overlay.Children.Add(element);
            flights.Add((element, tile, k));
        }
        // Measure all snapshots together. Updating each one after insertion can
        // trigger a full layout pass for every visible tile in the flight.
        overlay.UpdateLayout();
        foreach (var (element, tile, index) in flights)
            Animate(element, tile.Bounds, target, compositor, linear, stagger * index);
        batch.Completed += (_, _) =>
        {
            if (generation == _generation) Clear();
        };
        batch.End();
    }

    /// <summary>Cancels a flight still in the air and clears the overlay.</summary>
    public void Finish()
    {
        if (overlay.Children.Count == 0 && overlay.Visibility == Visibility.Collapsed) return;
        _generation++;
        Clear();
    }

    private void Clear()
    {
        foreach (var child in overlay.Children.OfType<FrameworkElement>())
        {
            var visual = ElementCompositionPreview.GetElementVisual(child);
            visual.StopAnimation("Translation");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Opacity");
        }
        overlay.Children.Clear();
        overlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// A plain thumbnail, not a faithful copy: the badges and the filename row
    /// are unreadable inside 180 ms, and rendering each real container to a
    /// bitmap would be asynchronous and far more expensive.
    /// </summary>
    private static Border CreateSnapshot(CapturedTile tile)
    {
        var element = new Border
        {
            Width = tile.Bounds.Width,
            Height = tile.Bounds.Height,
            CornerRadius = new CornerRadius(8),
            IsHitTestVisible = false,
            Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush,
            Child = new Image
            {
                Stretch = Stretch.Uniform,
                Source = LibraryBrowser.Thumbnail(tile.ThumbnailUri),
            },
        };
        Canvas.SetLeft(element, tile.Bounds.X);
        Canvas.SetTop(element, tile.Bounds.Y);
        return element;
    }

    private static void Animate(
        FrameworkElement element, FlightRect origin, FlightRect target,
        Compositor compositor, CompositionEasingFunction easing, TimeSpan delay)
    {
        // Translation rather than Offset: it composes with the Canvas position
        // instead of replacing it, so the start point needs no undoing.
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)(origin.Width / 2), (float)(origin.Height / 2), 0);

        var (dx, dy) = CollapseFlightGeometry.CenterDelta(origin, target);
        var scale = (float)CollapseFlightGeometry.UniformScale(origin, target);

        var move = compositor.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(1f, new Vector3((float)dx, (float)dy, 0), easing);
        move.Duration = CardMotion.ReturnDuration;
        move.DelayTime = delay;

        var shrink = compositor.CreateVector3KeyFrameAnimation();
        shrink.InsertKeyFrame(1f, new Vector3(Math.Max(scale, LandedScale), Math.Max(scale, LandedScale), 1f), easing);
        shrink.Duration = CardMotion.ReturnDuration;
        shrink.DelayTime = delay;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 0f, easing);
        fade.Duration = CardMotion.ReturnDuration;
        fade.DelayTime = delay;

        visual.StartAnimation("Translation", move);
        visual.StartAnimation("Scale", shrink);
        visual.StartAnimation("Opacity", fade);
    }

    private bool TryBounds(FrameworkElement element, out FlightRect bounds)
    {
        bounds = default;
        try
        {
            var origin = element.TransformToVisual(overlay).TransformPoint(new Point(0, 0));
            bounds = new(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
            return true;
        }
        catch (ArgumentException)
        {
            // An element that is not in the visual tree has no transform.
            return false;
        }
    }
}

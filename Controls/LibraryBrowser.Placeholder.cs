using KoikatsuSceneGallery.Helpers;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The pulse on a tile whose thumbnail does not exist yet.
/// </summary>
/// <remarks>
/// <para>Runs on the compositor: the scan's bottleneck is the UI thread, and a
/// storyboard or timer per tile would add to exactly that. Each placeholder
/// starts one composition animation when it appears and the UI thread does
/// nothing more for it.</para>
/// <para>Finite on purpose. A card whose thumbnail fails to generate never gets
/// one, and nothing marks the failure; an endless pulse would claim it is
/// still loading. After a few seconds the placeholder settles into a still
/// grey tile with a picture icon.</para>
/// </remarks>
public sealed partial class LibraryBrowser
{
    private const int PlaceholderPulses = 5;
    private static ScalarKeyFrameAnimation? _placeholderPulse;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object> WatchedPlaceholders = new();

    public static Visibility PlaceholderVisibility(Uri? thumbnail) =>
        thumbnail is null ? Visibility.Visible : Visibility.Collapsed;

    private void Placeholder_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement placeholder) return;
        // Containers are recycled; a placeholder that comes back for another
        // card has to pulse again, not stay where the last animation left it.
        // Loaded fires again whenever the grid is re-entered, so register once.
        if (!WatchedPlaceholders.TryGetValue(placeholder, out _))
        {
            WatchedPlaceholders.Add(placeholder, new object());
            placeholder.RegisterPropertyChangedCallback(VisibilityProperty, (element, _) =>
            {
                if (((UIElement)element).Visibility == Visibility.Visible) Pulse((UIElement)element);
            });
        }
        if (placeholder.Visibility == Visibility.Visible) Pulse(placeholder);
    }

    private static void Pulse(UIElement placeholder)
    {
        var visual = ElementCompositionPreview.GetElementVisual(placeholder);
        if (!CardMotion.AnimationsEnabled)
        {
            visual.Opacity = 1;
            return;
        }

        if (_placeholderPulse is null || _placeholderPulse.Compositor != visual.Compositor)
        {
            var pulse = visual.Compositor.CreateScalarKeyFrameAnimation();
            pulse.InsertKeyFrame(0f, 1f);
            pulse.InsertKeyFrame(0.5f, 0.45f);
            pulse.InsertKeyFrame(1f, 1f);
            pulse.Duration = CardMotion.PulseDuration;
            pulse.IterationBehavior = AnimationIterationBehavior.Count;
            pulse.IterationCount = PlaceholderPulses;
            _placeholderPulse = pulse;
        }

        visual.StartAnimation("Opacity", _placeholderPulse);
    }
}

using System.Numerics;
using KoikatsuSceneGallery.Controls;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace KoikatsuSceneGallery.XamlTestHost;

internal static class TagCloudMotionChecks
{
    internal static async Task RunAsync()
    {
        if (!CardMotion.AnimationsEnabled)
        {
            var motion = new TagCloudMotion();
            var tag = CreateTags(1)[0];
            var callbacks = 0;
            motion.Reveal(tag, [], () => callbacks++);
            motion.ReturnToCloud(tag, [], () => callbacks++);
            Require(callbacks == 2, "reduced motion completes click and return synchronously");
            Console.WriteLine("PASS: tag cloud reduced motion callbacks (animated checks skipped by system setting)");
            return;
        }

        // XAML's compositor does not finish detached animation batches, even
        // after RequestCommitAsync. Own a small test-only surface so these are
        // real completion callbacks without initializing the production App.
        var canvas = new Canvas();
        var loaded = Signal();
        canvas.Loaded += (_, _) => loaded.TrySetResult();
        var window = new Window { Title = "SceneGallery animation checks", Content = canvas };
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 560));
        try
        {
            window.Activate();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await CheckRevealHandoffAsync(canvas);
            await CheckFiniteCancellationAsync(canvas);
            await CheckDenseReturnAsync(canvas);
        }
        finally { window.Close(); }
    }

    private static async Task CheckRevealHandoffAsync(Canvas canvas)
    {
        var motion = new TagCloudMotion();
        var tags = CreateTags(3, canvas);
        try
        {
            motion.StartIdle(tags);
            // Give the tracked idle visuals a deterministic drift pose. Reading
            // a running composition animation would only expose its base value,
            // making a wall-clock sample an unreliable assertion of continuity.
            foreach (var tag in tags) SetPose(tag, new(3, -2, 0), new(1.02f, 1.02f, 1), 0.9f);

            var revealed = Signal();
            motion.Reveal(tags[0], tags.Skip(1), () => revealed.TrySetResult());
            foreach (var tag in tags)
            {
                var visual = ElementCompositionPreview.GetElementVisual(tag);
                visual.Properties.TryGetVector3("Translation", out var translation);
                Require(translation == new Vector3(3, -2, 0), "click preserves the drifting position");
                Require(visual.Scale == new Vector3(1.02f, 1.02f, 1), "click preserves the breathing scale");
                Require(Math.Abs(visual.Opacity - 0.9f) < 0.001f, "click preserves the breathing opacity");
            }
            await revealed.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var returned = Signal();
            motion.ReturnToCloud(tags[0], tags.Skip(1), () => returned.TrySetResult());
            await returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            motion.StopIdle();
            RequireRestingPose(tags);
            Console.WriteLine("PASS: tag cloud reveal handoff and return callbacks");
        }
        finally { motion.StopIdle(); }
    }

    private static async Task CheckFiniteCancellationAsync(Canvas canvas)
    {
        var motion = new TagCloudMotion();
        var tags = CreateTags(3, canvas);
        var abandonedCallbacks = 0;
        try
        {
            // These transitions deliberately start without idle first: stopping
            // navigation must own their visuals, not just previously idle tags.
            motion.Reveal(tags[0], tags.Skip(1), () => abandonedCallbacks++);
            foreach (var tag in tags) SetPose(tag, new(2, 3, 0), new(8, 8, 1), 0.2f);
            motion.StopIdle();
            RequireRestingPose(tags);

            motion.ReturnToCloud(tags[0], tags.Skip(1), () => abandonedCallbacks++);
            foreach (var tag in tags) SetPose(tag, new(-2, 3, 0), new(0.5f, 0.5f, 1), 0.3f);
            motion.StopIdle();
            RequireRestingPose(tags);

            // Let a real independent composition batch finish so cancellation
            // also proves stale callbacks cannot run later on the dispatcher.
            await WaitForCompositorAsync(tags[0]);
            Require(abandonedCallbacks == 0, "canceled click and return callbacks stay canceled");
            Console.WriteLine("PASS: tag cloud finite transition cancellation and reset");
        }
        finally { motion.StopIdle(); }
    }

    private static async Task CheckDenseReturnAsync(Canvas canvas)
    {
        var motion = new TagCloudMotion();
        var tags = CreateTags(160, canvas);
        try
        {
            foreach (var tag in tags) SetPose(tag, new(2, 1, 0), new(0.45f, 0.45f, 1), 0);
            var returned = Signal();
            motion.ReturnToCloud(tags[0], tags.Skip(1), () => returned.TrySetResult());
            // 460 ms + a bounded stagger has ample CI slack here. The previous
            // per-tag stagger took over 2.5 s for this cloud and misses the bound.
            await returned.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Console.WriteLine("PASS: tag cloud dense return has bounded duration");
        }
        finally { motion.StopIdle(); }
    }

    private static Button[] CreateTags(int count, Canvas? canvas = null)
    {
        canvas?.Children.Clear();
        var tags = Enumerable.Range(0, count).Select(index =>
        {
            var tag = new Button { Width = 80, Height = 28, FontSize = 18 };
            Canvas.SetLeft(tag, index % 10 * 88);
            Canvas.SetTop(tag, index / 10 * 30);
            canvas?.Children.Add(tag);
            tag.Measure(new Size(80, 28));
            return tag;
        }).ToArray();
        canvas?.UpdateLayout();
        return tags;
    }

    private static void SetPose(Button tag, Vector3 translation, Vector3 scale, float opacity)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(tag, true);
        var visual = ElementCompositionPreview.GetElementVisual(tag);
        visual.StopAnimation("Translation");
        visual.StopAnimation("Scale");
        visual.StopAnimation("Opacity");
        visual.Properties.InsertVector3("Translation", translation);
        visual.Scale = scale;
        visual.Opacity = opacity;
    }

    private static void RequireRestingPose(IEnumerable<Button> tags)
    {
        foreach (var tag in tags)
        {
            var visual = ElementCompositionPreview.GetElementVisual(tag);
            visual.Properties.TryGetVector3("Translation", out var translation);
            Require(translation == Vector3.Zero && visual.Scale == Vector3.One && visual.Opacity == 1,
                "stopping a finite transition resets its visual");
        }
    }

    private static async Task WaitForCompositorAsync(Button tag)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(tag).Compositor;
        var visual = ElementCompositionPreview.GetElementVisual(tag);
        using var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, 0);
        animation.Duration = TimeSpan.FromMilliseconds(650);
        using var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var completed = Signal();
        batch.Completed += (_, _) => completed.TrySetResult();
        visual.StartAnimation("Opacity", animation);
        batch.End();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Require(bool value, string operation)
    {
        if (!value) throw new InvalidOperationException($"Tag cloud motion check failed: {operation}");
    }
}

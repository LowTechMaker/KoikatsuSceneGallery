using System.Collections.ObjectModel;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.XamlTestHost;

internal static class GalleryBaseChecks
{
    internal static async Task RunAsync()
    {
        using var scheduler = new ThumbnailPriorityScheduler(capacity: 1, workerCount: 1);
        var first = new CoordinateCard { FilePath = "a.png", Width = 32, Height = 32 };
        var second = new CoordinateCard { FilePath = "b.png", Width = 64, Height = 64 };
        var third = new CoordinateCard { FilePath = "c.png", Width = 32, Height = 32 };
        var source = new ObservableCollection<CoordinateCard> { first, second, third };
        var gallery = new TestGallery(source, scheduler);
        var independent = new TestGallery(new([first, second, third]), scheduler);
        try
        {
            gallery.ChangeResolution(true, ["32x32"]);
            await DrainDispatcherAsync();
            Require(gallery.CardsView.Count == 2, "resolution filter");
            Require(independent.CardsView.Count == 3, "per-gallery state isolation");
            for (int i = 0; i < 20; i++)
            {
                var chosen = gallery.Choose();
                Require(ReferenceEquals(chosen, first) || ReferenceEquals(chosen, third), "random choice must be visible");
            }
            Console.WriteLine("PASS: gallery visible random choice and isolated resolution state");

            gallery.SetShuffleDisplayCount(20);
            gallery.SelectedSort = SortOption.Shuffle;
            gallery.ChangeResolution(true, ["64x64"]);
            await DrainDispatcherAsync();
            Require(gallery.CardsView.Count == 1 && ReferenceEquals(gallery.Choose(), second), "shuffle queue rebuilt for resolution");
            gallery.ChangeResolution(false, ["32x32"]);
            await DrainDispatcherAsync();
            Require(gallery.CardsView.Count == 3, "disabled resolution filter");
            gallery.ChangeResolution(true, []);
            await DrainDispatcherAsync();
            Require(gallery.CardsView.Count == 3, "empty resolution set is unrestricted");
            Console.WriteLine("PASS: gallery shuffle resolution updates");

            gallery.SetShuffleDisplayCount(1);
            var previous = gallery.Choose();
            Require(gallery.CardsView.Count == 1 && previous is not null, "one visible shuffle card");
            gallery.Reshuffle();
            Require(gallery.CardsView.Count == 1 && gallery.Choose() is { } next
                && !ReferenceEquals(previous, next), "shuffle advances to the retained tail");
            gallery.SelectedSort = SortOption.Name;
            Require(gallery.CardsView.Count == 3 && ReferenceEquals(gallery.CardsView[0], first), "leaving shuffle restores name sorting");
            gallery.SortAscending = false;
            Require(ReferenceEquals(gallery.CardsView[0], third), "descending name sorting");
            Console.WriteLine("PASS: gallery shuffle rotation");

            source.Clear();
            Require(gallery.Choose() is null, "random choice on empty view");
            Console.WriteLine("PASS: gallery empty random choice");
        }
        finally
        {
            gallery.CancelPendingWork();
            independent.CancelPendingWork();
        }
    }

    private static async Task DrainDispatcherAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.GetForCurrentThread().TryEnqueue(() => done.SetResult()))
            throw new InvalidOperationException("Dispatcher barrier rejected");
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static void Require(bool value, string operation)
    {
        if (!value) throw new InvalidOperationException("Gallery base check failed: " + operation);
    }

    // Supplies only the abstract card predicate; all queue, sort, random and state code
    // under test comes from the production GalleryViewModelBase assembly.
    private sealed class TestGallery : GalleryViewModelBase
    {
        internal TestGallery(ObservableCollection<CoordinateCard> cards, ThumbnailPriorityScheduler scheduler)
            : base(cards, scheduler) => ApplyFilter();

        internal CoordinateCard? Choose() => GetRandomVisibleCard<CoordinateCard>();
        internal void ChangeResolution(bool enabled, HashSet<string> values) => OnResolutionFilterChanged(enabled, values);
        protected override bool CardPassesFilter(object card) => card is CoordinateCard coordinate
            && (!HasResolutionFilter || _allowedResolutions.Contains(coordinate.Resolution));
        protected override void ApplyFilter()
        {
            if (TryApplyShuffleFilter()) return;
            CardsView.Filter = CardPassesFilter;
            RefreshFilterAndNotify();
        }
    }
}

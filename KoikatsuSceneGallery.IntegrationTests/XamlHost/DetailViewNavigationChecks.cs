using System.Collections.ObjectModel;
using CommunityToolkit.WinUI.Collections;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.XamlTestHost;

internal static class DetailViewNavigationChecks
{
    internal static void Run()
    {
        var first = new CoordinateCard { FilePath = "a.png" };
        var hidden = new CoordinateCard { FilePath = "b.png" };
        var last = new CoordinateCard { FilePath = "c.png" };
        var source = new ObservableCollection<CardBase> { first, hidden, last };
        var view = new AdvancedCollectionView(source, true)
        {
            Filter = item => !ReferenceEquals(item, hidden)
        };
        view.SortDescriptions.Add(new SortDescription(nameof(CardBase.FileName), SortDirection.Descending));
        Require(view.Count == 2 && ReferenceEquals(view[0], last), "filtered and sorted view");
        Require(DetailNavigationHelper.GetNavigationState(view, last) == (false, true), "first visible state");
        Require(DetailNavigationHelper.GetNavigationState(view, first) == (true, false), "last visible state");
        Require(DetailNavigationHelper.GetNavigationState(view, hidden) == (false, false), "hidden current state");
        Require(DetailNavigationHelper.GetNavigationState(view, null) == (false, false), "null current state");
        Require(ReferenceEquals(DetailNavigationHelper.Navigate(view, last, 1), first), "next follows view order");
        Require(ReferenceEquals(DetailNavigationHelper.Navigate(view, first, -1), last), "previous follows view order");
        Require(DetailNavigationHelper.Navigate(view, first, 1) is null, "end boundary");
        Require(DetailNavigationHelper.Navigate<CoordinateCard>(view, null, 1) is null, "null current navigation");
        Require(ReferenceEquals(DetailNavigationHelper.Navigate(view, hidden, 1), last), "missing index policy");
        Console.WriteLine("PASS: detail view navigation follows visible order");

        Require(ReferenceEquals(DetailNavigationHelper.RandomCard(view, first), last), "random excludes current and hidden");
        Require(ReferenceEquals(DetailNavigationHelper.RandomCard(view, last), first), "random picks only other visible card");
        Require(ReferenceEquals(DetailNavigationHelper.FindAdjacentOnRemoval(view, last), first), "removal prefers next");
        Require(ReferenceEquals(DetailNavigationHelper.FindAdjacentOnRemoval(view, first), last), "removal falls back to previous");
        Require(DetailNavigationHelper.FindAdjacentOnRemoval(view, hidden) is null, "missing removal has no adjacent card");
        Console.WriteLine("PASS: detail view random and removal candidates");

        source.Clear();
        Require(DetailNavigationHelper.GetNavigationState(view, first) == (false, false), "empty state");
        Require(DetailNavigationHelper.RandomCard(view, first) is null, "empty random");
        source.Add(first);
        Require(ReferenceEquals(DetailNavigationHelper.RandomCard(view, first), first), "single visible card may repeat");
        Require(DetailNavigationHelper.FindAdjacentOnRemoval(view, first) is null, "single card removal");
        source.Clear();
        source.Add(new MediaCard { FilePath = "media.png" });
        Require(DetailNavigationHelper.RandomCard(view, first) is null, "random preserves requested card type");
        Require(DetailNavigationHelper.Navigate(view, first, 1) is null, "navigation preserves requested card type");
        Console.WriteLine("PASS: detail view empty single and mismatched type");

        RunScopeSelection(first, hidden, last);
    }

    // The scope overloads the detail pages call: a non-null scope wins outright,
    // a null scope falls through to the visible collection. The scoped list is
    // deliberately ordered against the view so a wrong source cannot pass.
    private static void RunScopeSelection(CoordinateCard first, CoordinateCard hidden, CoordinateCard last)
    {
        var source = new ObservableCollection<CardBase> { first, hidden, last };
        var view = new AdvancedCollectionView(source, true)
        {
            Filter = item => !ReferenceEquals(item, hidden)
        };
        view.SortDescriptions.Add(new SortDescription(nameof(CardBase.FileName), SortDirection.Descending));
        var scoped = new List<CoordinateCard> { first, hidden };

        Require(DetailNavigationHelper.GetNavigationState(null, view, last) == (false, true),
            "null scope uses the view state");
        Require(DetailNavigationHelper.GetNavigationState(scoped, view, last) == (false, false),
            "scope excludes a card the view shows");
        Require(DetailNavigationHelper.GetNavigationState(scoped, view, hidden) == (true, false),
            "scope includes a card the view filters out");

        Require(ReferenceEquals(DetailNavigationHelper.Navigate(null, view, last, 1), first),
            "null scope navigates the view");
        Require(ReferenceEquals(DetailNavigationHelper.Navigate(scoped, view, first, 1), hidden),
            "scope navigates its own order");
        // A card outside the scope has index -1, so forward navigation lands on
        // the first scoped card. That list policy is fixed by the 54th part and
        // the scope overload must not quietly change it.
        Require(ReferenceEquals(DetailNavigationHelper.Navigate(scoped, view, last, 1), first),
            "scope keeps the list policy for a card it does not contain");

        Require(ReferenceEquals(DetailNavigationHelper.RandomCard(null, view, first), last),
            "null scope draws from the view");
        Require(ReferenceEquals(DetailNavigationHelper.RandomCard(scoped, view, first), hidden),
            "scope draws only from its own cards");
        Console.WriteLine("PASS: detail view scope selection");
    }

    private static void Require(bool value, string operation)
    {
        if (!value) throw new InvalidOperationException("Detail view check failed: " + operation);
    }
}

using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportReviewWorkspaceTests
{
    private static string Text(string key) => key switch
    {
        "Import_Review_GroupTitle" => "{0} ({1})",
        _ when key.EndsWith("Count") => key + " {0}",
        _ => key
    };

    private static ImportItem Item(string name, ContentRating rating = ContentRating.AllAges) => new()
    {
        SourceFilePath = Path.Combine(Path.GetTempPath(), "review", name + ".png"),
        Rating = rating,
        Status = ImportItemStatus.ReadyToImport
    };

    private sealed class Harness : IDisposable
    {
        public Queue<Action> Queue { get; } = new();
        public ImportReviewWorkspace Workspace { get; }
        public Harness() => Workspace = new(Text, Queue.Enqueue);
        public void Flush() { while (Queue.TryDequeue(out var action)) action(); }
        public void Dispose() => Workspace.Dispose();
    }

    [Fact]
    public void ReconcilePreservesIdentityAndUnsubscribesRemovedStates()
    {
        using var h = new Harness();
        var a = Item("a");
        var b = Item("b");
        h.Workspace.Reconcile([a, b]);
        var state = h.Workspace.States[0];
        state.IsSelected = true;
        h.Workspace.Reconcile([a]);
        Assert.Same(state, Assert.Single(h.Workspace.States));
        Assert.True(state.IsSelected);
        var stateNotifications = 0;
        state.PropertyChanged += (_, _) => stateNotifications++;
        h.Workspace.Reconcile([b]);
        h.Flush();
        var changes = 0;
        h.Workspace.Changed += () => changes++;
        a.Rating = ContentRating.R18;
        Assert.Equal(0, stateNotifications);
        state.IsSelected = false;
        Assert.Equal(0, changes);
        Assert.Same(b, Assert.Single(h.Workspace.States).Item);
    }

    [Fact]
    public void DistinctItemInstancesWithSamePathRemainDistinct()
    {
        using var h = new Harness();
        var a = Item("a");
        var other = Item("a");
        h.Workspace.Reconcile([a, a, other]);
        Assert.Equal(2, h.Workspace.States.Count);
    }

    [Theory]
    [InlineData("AllAges", ContentRating.AllAges)]
    [InlineData("R18", ContentRating.R18)]
    [InlineData("R18G", ContentRating.R18G)]
    public void FiltersAndReapplyingFilterClearSelection(string filter, ContentRating rating)
    {
        using var h = new Harness();
        h.Workspace.Reconcile([Item("g"), Item("r", ContentRating.R18), Item("rg", ContentRating.R18G)]);
        h.Workspace.SelectAll(true);
        h.Workspace.SetFilter(filter);
        Assert.Equal(rating, Assert.Single(h.Workspace.VisibleItems).Item.Rating);
        Assert.All(h.Workspace.States, state => Assert.False(state.IsSelected));
        h.Workspace.SelectAll(true);
        Assert.Equal(1, h.Workspace.SelectedCount);
        h.Workspace.SetFilter(filter);
        Assert.Equal(0, h.Workspace.SelectedCount);
    }

    [Fact]
    public void SelectionUsesOnlyVisibleSelectableItemsAndTracksThreeStates()
    {
        using var h = new Harness();
        var existing = Item("existing");
        existing.Status = ImportItemStatus.AlreadyInLibrary;
        h.Workspace.Reconcile([Item("a"), Item("b"), existing, Item("hidden", ContentRating.R18)]);
        h.Workspace.SetFilter("AllAges");
        Assert.Equal(false, h.Workspace.AllSelected);
        h.Workspace.States[0].IsSelected = true;
        Assert.Null(h.Workspace.AllSelected);
        h.Workspace.SelectAll(null);
        Assert.Equal(1, h.Workspace.SelectedCount);
        h.Workspace.SelectAll(true);
        Assert.Equal(true, h.Workspace.AllSelected);
        Assert.Equal(2, h.Workspace.SelectedItems.Count);
        Assert.False(h.Workspace.States[2].IsSelected);
        Assert.False(h.Workspace.States[3].IsSelected);
        h.Workspace.States[0].Item.Status = ImportItemStatus.AlreadyInLibrary;
        Assert.Equal(1, h.Workspace.SelectedCount);
        h.Workspace.SelectAll(false);
        Assert.Equal(false, h.Workspace.AllSelected);
    }

    [Fact]
    public void ItemRatingChangesRefreshVisibleProjection()
    {
        using var h = new Harness();
        var a = Item("a");
        h.Workspace.Reconcile([a]);
        h.Workspace.SetFilter("R18");
        Assert.Empty(h.Workspace.VisibleItems);
        a.Rating = ContentRating.R18;
        Assert.Same(a, Assert.Single(h.Workspace.VisibleItems).Item);
    }

    [Fact]
    public void RowAndGroupTogglesSelectTheirMembersOnly()
    {
        using var h = new Harness();
        var items = Enumerable.Range(0, 5).Select(i => Item(i.ToString())).ToArray();
        foreach (var item in items) item.AuthorId = "author";
        h.Workspace.Reconcile(items);
        h.Flush();
        var row = h.Workspace.Rows.First(row => row.Kind == ImportReviewRowKind.GridRow);
        h.Workspace.Toggle(row.Items);
        Assert.Equal(4, h.Workspace.SelectedCount);
        var group = h.Workspace.Rows.Single(row => row.Kind == ImportReviewRowKind.Group);
        h.Workspace.Toggle(group.Items);
        Assert.Equal(5, h.Workspace.SelectedCount);
        h.Workspace.Toggle(group.Items);
        Assert.Equal(0, h.Workspace.SelectedCount);
    }

    [Fact]
    public void RowsAreCoalescedReusedAndReorderedAcrossGroupChanges()
    {
        using var h = new Harness();
        var a = Item("a");
        var b = Item("b");
        b.AuthorId = "author";
        h.Workspace.Reconcile([a, b]);
        h.Workspace.Reconcile([a, b]);
        h.Workspace.SetReviewViewportWidth(1000);
        Assert.Single(h.Queue);
        h.Flush();
        var section = h.Workspace.Rows.Single(row => row.Key == "Identified:");
        var group = h.Workspace.Rows.Single(row => row.Kind == ImportReviewRowKind.Group && row.Items.Contains(h.Workspace.States[1]));
        var oldIndex = h.Workspace.Rows.IndexOf(group);
        a.AuthorId = "author";
        h.Flush();
        Assert.Same(section, h.Workspace.Rows.Single(row => row.Key == "Identified:"));
        Assert.Same(group, h.Workspace.Rows.Single(row => row.Kind == ImportReviewRowKind.Group));
        Assert.True(h.Workspace.Rows.IndexOf(group) < oldIndex);
        Assert.Equal(2, group.Items.Count);
        Assert.Contains("2", section.Title);
    }

    [Fact]
    public void WidthChangesRechunkAndReuseMatchingRows()
    {
        using var h = new Harness();
        var items = Enumerable.Range(0, 5).Select(i => Item(i.ToString())).ToArray();
        foreach (var item in items) item.AuthorId = "author";
        h.Workspace.Reconcile(items);
        h.Flush();
        var first = h.Workspace.Rows.First(row => row.Kind == ImportReviewRowKind.GridRow);
        h.Workspace.SetReviewViewportWidth(double.NaN);
        h.Workspace.SetReviewViewportWidth(32);
        Assert.Empty(h.Queue);
        h.Workspace.SetReviewViewportWidth(472);
        h.Flush();
        Assert.Same(first, h.Workspace.Rows.First(row => row.Kind == ImportReviewRowKind.GridRow));
        Assert.Equal(3, h.Workspace.Rows.Count(row => row.Kind == ImportReviewRowKind.GridRow));
        Assert.All(h.Workspace.States, state => Assert.Equal(214, state.TileWidth));
        h.Workspace.SetReviewViewportWidth(472);
        Assert.Empty(h.Queue);
    }

    [Fact]
    public void ClearInvalidatesOldQueueAndAllowsNewPublication()
    {
        using var h = new Harness();
        var old = Item("old");
        h.Workspace.Reconcile([old]);
        var state = h.Workspace.States[0];
        h.Workspace.SetFilter("R18");
        h.Workspace.Clear();
        Assert.Equal("All", h.Workspace.Filter);
        Assert.Empty(h.Workspace.Rows);
        h.Workspace.Reconcile([Item("new")]);
        Assert.Equal(2, h.Queue.Count);
        h.Queue.Dequeue()();
        Assert.Empty(h.Workspace.Rows);
        h.Flush();
        Assert.DoesNotContain(h.Workspace.Rows.SelectMany(row => row.Items), s => ReferenceEquals(s, state));
        var changes = 0;
        h.Workspace.Changed += () => changes++;
        old.Rating = ContentRating.R18G;
        Assert.Equal(0, changes);
    }

    [Fact]
    public void DisposeDetachesSubscriptionsAndInvalidatesQueuedWork()
    {
        var h = new Harness();
        var item = Item("a");
        h.Workspace.Reconcile([item]);
        var state = h.Workspace.States[0];
        h.Dispose();
        h.Dispose();
        var changes = 0;
        state.PropertyChanged += (_, _) => changes++;
        item.AuthorName = "new";
        h.Flush();
        Assert.Equal(0, changes);
        Assert.Empty(h.Workspace.Rows);
        Assert.Throws<ObjectDisposedException>(() => h.Workspace.Reconcile([item]));
    }

    [Fact]
    public void ReviewStateUsesInjectedTextResolver()
    {
        using var state = new ImportItemReviewState(Item("a"), "unassigned", key => "text:" + key);
        Assert.Equal("unassigned", state.DisplayAuthorName);
        Assert.StartsWith("text:Import_Status_", state.StatusText);
        Assert.Equal(state.StatusText, state.DestinationText);
    }
}

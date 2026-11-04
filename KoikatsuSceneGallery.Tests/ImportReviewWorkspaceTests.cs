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
    public void SetSelectionSelectsExactlyTheChosenCardsWithOneNotification()
    {
        using var h = new Harness();
        var items = Enumerable.Range(0, 200).Select(i => Item($"card{i}")).ToArray();
        h.Workspace.Reconcile(items);
        h.Flush();
        h.Workspace.States[0].IsSelected = true; // a stale selection that must be replaced
        var changes = 0;
        h.Workspace.Changed += () => changes++;
        var wanted = new HashSet<ImportItem>(items.Skip(10).Take(50));

        h.Workspace.SetSelection(state => wanted.Contains(state.Item));

        Assert.Equal(1, changes);
        Assert.Equal(50, h.Workspace.SelectedCount);
        Assert.Equal(wanted, h.Workspace.SelectedItems.ToHashSet());
        Assert.False(h.Workspace.States[0].IsSelected);
    }

    [Fact]
    public void SetSelectionLeavesCardsAlreadyInTheLibraryUnselected()
    {
        using var h = new Harness();
        var inLibrary = Item("owned");
        inLibrary.Status = ImportItemStatus.AlreadyInLibrary;
        var fresh = Item("fresh");
        h.Workspace.Reconcile([inLibrary, fresh]);
        h.Flush();

        h.Workspace.SetSelection(_ => true);

        Assert.Same(fresh, Assert.Single(h.Workspace.SelectedItems));
    }

    [Fact]
    public void SettingAPropertyOnEveryCardOfALargeListIsNotQuadratic()
    {
        using var h = new Harness();
        var items = Enumerable.Range(0, 2000).Select(i =>
        {
            var item = Item("big" + i);
            item.DestinationPath = "C:/lib/a/" + i + ".png";
            return item;
        }).ToArray();
        h.Workspace.Reconcile(items);
        h.Flush();

        // This took about six seconds before the visible list stopped being re-sorted for
        // every property change; the bound is generous so a slow machine does not fail it.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var item in items) item.DestinationPath = "C:/lib/b/" + item.FileName;
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 1500, $"took {watch.ElapsedMilliseconds} ms");
        Assert.Equal(2000, h.Workspace.VisibleItems.Count);
    }

    [Fact]
    public void ACardWhoseRatingNoLongerMatchesTheFilterLeavesTheVisibleListAtOnce()
    {
        using var h = new Harness();
        var stays = Item("stays");
        var leaves = Item("leaves");
        h.Workspace.Reconcile([stays, leaves]);
        h.Workspace.SetFilter("AllAges");
        h.Flush();

        leaves.Rating = ContentRating.R18;

        Assert.Same(stays, Assert.Single(h.Workspace.VisibleItems).Item);
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
    public void ToggleSectionOnlySelectsOneSectionAndTogglesItOff()
    {
        using var h = new Harness();
        var unidentified = Item("unidentified");
        var secondUnidentified = Item("second-unidentified");
        var unavailable = Item("unavailable");
        unavailable.ArtworkId = new("test", "artwork");
        var identified = Item("identified");
        identified.AuthorId = "author";
        h.Workspace.Reconcile([unidentified, secondUnidentified, unavailable, identified]);
        h.Flush();

        var unidentifiedRow = h.Workspace.Rows.Single(row => row.Key == "Unidentified:");
        var unavailableRow = h.Workspace.Rows.Single(row => row.Key == "Unavailable:");
        var identifiedRow = h.Workspace.Rows.Single(row => row.Key == "Identified:");

        h.Workspace.ToggleSectionOnly(unidentifiedRow.Items);
        Assert.Equal(2, h.Workspace.SelectedItems.Count);
        Assert.Contains(unidentified, h.Workspace.SelectedItems);
        Assert.Contains(secondUnidentified, h.Workspace.SelectedItems);
        h.Workspace.ToggleSectionOnly(unidentifiedRow.Items);
        Assert.Empty(h.Workspace.SelectedItems);

        h.Workspace.ToggleSectionOnly(identifiedRow.Items);
        Assert.Same(identified, Assert.Single(h.Workspace.SelectedItems));
        h.Workspace.ToggleSectionOnly(unavailableRow.Items);
        Assert.Same(unavailable, Assert.Single(h.Workspace.SelectedItems));
        h.Workspace.ToggleSectionOnly(unavailableRow.Items);
        Assert.Empty(h.Workspace.SelectedItems);
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
        var row = h.Workspace.Rows.First(row => row.Kind == ImportReviewRowKind.ListItem);
        h.Workspace.Toggle(row.Items);
        Assert.Equal(1, h.Workspace.SelectedCount);
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
    public void ModsGetTheirOwnSectionBeforeTheSkippedCards()
    {
        using var h = new Harness();
        var skipped = Item("owned");
        skipped.Status = ImportItemStatus.AlreadyInLibrary;
        var ready = Item("ready");
        ready.AuthorId = "author";
        h.Workspace.Reconcile([skipped, ready]);
        var mod = new ModReviewItem(new Services.ModInstallCandidate("m.zipmod", new Services.ZipmodManifest("g", "Mod", "1", null, [])), null);
        h.Workspace.SetMods([mod]);
        h.Flush();

        Assert.Equal(
            [ImportReviewSection.Identified, ImportReviewSection.Mods, ImportReviewSection.Skipped],
            h.Workspace.Rows.Where(r => r.Kind == ImportReviewRowKind.Section).Select(r => r.Section));
        Assert.Same(mod, h.Workspace.Rows.Single(r => r.Kind == ImportReviewRowKind.ModItem).Mod);
        // The skipped section starts collapsed: header only.
        Assert.DoesNotContain(h.Workspace.Rows, r => r.Section == ImportReviewSection.Skipped && r.Kind == ImportReviewRowKind.ListItem);

        h.Workspace.ToggleCollapsed(ImportReviewSection.Skipped);
        h.Flush();
        Assert.Contains(h.Workspace.Rows, r => r.Section == ImportReviewSection.Skipped && r.Kind == ImportReviewRowKind.ListItem);
    }

    [Fact]
    public void ASectionHeaderReportsAllSomeOrNoneSelected()
    {
        using var h = new Harness();
        var items = Enumerable.Range(0, 3).Select(i => Item(i.ToString())).ToArray();
        h.Workspace.Reconcile(items);
        h.Flush();
        var header = h.Workspace.Rows.Single(r => r.Kind == ImportReviewRowKind.Section);

        Assert.False(header.SelectionState);
        h.Workspace.States[0].IsSelected = true;
        Assert.Null(header.SelectionState);
        h.Workspace.SelectAll(true);
        Assert.True(header.SelectionState);
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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Tests;

public sealed class CollectionItemObserverTests
{
    private sealed class Item : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Change() => PropertyChanged?.Invoke(this, new("IsSelected"));
        // Value equality must not collapse distinct notification sources.
        public override bool Equals(object? obj) => obj is Item;
        public override int GetHashCode() => 0;
    }

    [Fact]
    public void ReplaceUnsubscribesOldAndSubscribesNewBeforeCollectionCallback()
    {
        var old = new Item();
        var replacement = new Item();
        var source = new ObservableCollection<Item> { old };
        var events = new List<object?>();
        using var observer = new CollectionItemObserver<Item>(source, (s, _) => events.Add(s), () => replacement.Change());
        source[0] = replacement;
        old.Change();
        Assert.Same(replacement, Assert.Single(events));
    }

    [Fact]
    public void AddRemoveAndClearTrackDistinctInstances()
    {
        var source = new ObservableCollection<Item>();
        var first = new Item();
        var second = new Item();
        var events = new List<object?>();
        var changes = 0;
        using var observer = new CollectionItemObserver<Item>(source, (s, _) => events.Add(s), () => changes++);
        source.Add(first);
        source.Add(second);
        first.Change(); second.Change();
        source.RemoveAt(0);
        first.Change(); second.Change();
        source.Clear();
        second.Change();
        Assert.Equal(3, events.Count);
        Assert.Same(first, events[0]);
        Assert.Same(second, events[1]);
        Assert.Same(second, events[2]);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void DuplicateInstanceStaysSubscribedUntilLastOccurrenceIsRemoved()
    {
        var item = new Item();
        var source = new ObservableCollection<Item> { item, item };
        var count = 0;
        using var observer = new CollectionItemObserver<Item>(source, (_, _) => count++, () => { });
        item.Change();
        source.RemoveAt(0);
        item.Change();
        source.Clear();
        item.Change();
        Assert.Equal(2, count);
    }

    [Fact]
    public void MoveDoesNotDuplicateSubscriptions()
    {
        var item = new Item();
        var source = new ObservableCollection<Item> { item, new() };
        var count = 0;
        var changes = 0;
        using var observer = new CollectionItemObserver<Item>(source, (_, _) => count++, () => changes++);
        source.Move(0, 1);
        item.Change();
        Assert.Equal(1, count);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void DisposeUnsubscribesBothCollectionAndItemsAndIsIdempotent()
    {
        var item = new Item();
        var source = new ObservableCollection<Item> { item };
        var count = 0;
        var observer = new CollectionItemObserver<Item>(source, (_, _) => count++, () => count++);
        observer.Dispose(); observer.Dispose();
        item.Change();
        source.Add(new());
        source[1].Change();
        Assert.Equal(0, count);
    }

    [Fact]
    public void RemovedImportItemCannotPublishLateReadyOrDestinationChanges()
    {
        var item = new ImportItem { SourceFilePath = "removed.png", Status = ImportItemStatus.Analyzing };
        var source = new ObservableCollection<ImportItem> { item };
        var notifications = new List<string?>();
        NotifyCollectionChangedEventArgs? removal = null;
        using var observer = new CollectionItemObserver<ImportItem>(source,
            (_, e) => notifications.Add(e.PropertyName), e =>
            {
                removal = e;
                // Even synchronous changes made by the owner's removal handler are detached.
                item.Status = ImportItemStatus.ReadyToImport;
            });
        source.Remove(item);
        item.DestinationPath = "late.png";
        Assert.Empty(notifications);
        Assert.Equal(NotifyCollectionChangedAction.Remove, removal!.Action);
        Assert.Same(item, Assert.Single(removal.OldItems!.Cast<ImportItem>()));
    }

    [Fact]
    public void AddedImportItemIsSubscribedBeforePlacementAndResetAllowsReuse()
    {
        var source = new ObservableCollection<ImportItem>();
        var item = new ImportItem { SourceFilePath = "reused.png", Status = ImportItemStatus.Analyzing };
        var notifications = 0;
        var actions = new List<NotifyCollectionChangedAction>();
        using var observer = new CollectionItemObserver<ImportItem>(source,
            (_, e) => { if (e.PropertyName == nameof(ImportItem.Status)) notifications++; }, e =>
            {
                actions.Add(e.Action);
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    Assert.Same(item, Assert.Single(e.NewItems!.Cast<ImportItem>()));
                    item.Status = ImportItemStatus.ReadyToImport;
                }
            });
        source.Add(item);
        source.Clear();
        item.Status = ImportItemStatus.Analyzing;
        source.Add(item);
        Assert.Equal(2, notifications);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Reset,
            NotifyCollectionChangedAction.Add }, actions);
    }
}

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace KoikatsuSceneGallery.Helpers;

// Collection and item notifications must be serialized by the owner (UI thread).
// One subscription per instance, even when the collection contains it multiple times.
internal sealed class CollectionItemObserver<T> : IDisposable where T : class, INotifyPropertyChanged
{
    private readonly ObservableCollection<T> _source;
    private readonly PropertyChangedEventHandler _itemChanged;
    private readonly Action<NotifyCollectionChangedEventArgs> _collectionChanged;
    private readonly HashSet<T> _subscribed = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public CollectionItemObserver(ObservableCollection<T> source,
        PropertyChangedEventHandler itemChanged, Action collectionChanged)
        : this(source, itemChanged, _ => collectionChanged())
    {
    }

    public CollectionItemObserver(ObservableCollection<T> source,
        PropertyChangedEventHandler itemChanged, Action<NotifyCollectionChangedEventArgs> collectionChanged)
    {
        _source = source;
        _itemChanged = itemChanged;
        _collectionChanged = collectionChanged;
        Reconcile();
        _source.CollectionChanged += OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Reconcile();
        _collectionChanged(e);
    }

    private void Reconcile()
    {
        var current = new HashSet<T>(_source, ReferenceEqualityComparer.Instance);
        foreach (var item in _subscribed.ToArray())
            if (!current.Contains(item))
            {
                item.PropertyChanged -= _itemChanged;
                _subscribed.Remove(item);
            }
        foreach (var item in current)
            if (_subscribed.Add(item)) item.PropertyChanged += _itemChanged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.CollectionChanged -= OnCollectionChanged;
        foreach (var item in _subscribed) item.PropertyChanged -= _itemChanged;
        _subscribed.Clear();
    }
}

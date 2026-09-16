using System.Collections.ObjectModel;
using System.Collections.Specialized;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery.Helpers;

public enum LibraryKind { Scenes, Characters, Coordinates, Screenshots }

internal interface ILibrary
{
    LibraryKind Kind { get; }
    GalleryViewModelBase ViewModel { get; }
    IReadOnlyList<CardBase> Cards { get; }
    string Title { get; }
    double ImageRatio { get; }
    bool GroupingEnabled { get; }
    bool IsParsing { get; }
    event NotifyCollectionChangedEventHandler? CardsChanged;
    bool Supports(CardBase card);
    Task LoadAsync();
    void Thumbnail(CardBase card, bool release = false);
    void WatchFolders(Action callback);
}

// Bind once at composition time instead of dispatching on VM/card pairs in consumers.
internal sealed class LibraryAdapter<TCard>(
    LibraryKind kind, GalleryViewModelBase viewModel, ObservableCollection<TCard> cards,
    string titleKey, double imageRatio, bool groupingEnabled,
    Func<bool> isParsing, Func<Task> load, Action<TCard> requestThumbnail,
    Action<TCard> releaseThumbnail, Action<Action> watchFolders) : ILibrary where TCard : CardBase
{
    public LibraryKind Kind { get; } = kind;
    public GalleryViewModelBase ViewModel { get; } = viewModel;
    public IReadOnlyList<CardBase> Cards => cards;
    public string Title => UiText.Get(titleKey);
    public double ImageRatio { get; } = imageRatio;
    public bool GroupingEnabled { get; } = groupingEnabled;
    public bool IsParsing => isParsing();
    public event NotifyCollectionChangedEventHandler? CardsChanged
    {
        add => cards.CollectionChanged += value;
        remove => cards.CollectionChanged -= value;
    }
    public bool Supports(CardBase card) => card is TCard;
    public Task LoadAsync() => load();
    public void Thumbnail(CardBase card, bool release = false)
    {
        if (card is not TCard typed) return;
        if (release) releaseThumbnail(typed); else requestThumbnail(typed);
    }
    public void WatchFolders(Action callback) => watchFolders(callback);
}

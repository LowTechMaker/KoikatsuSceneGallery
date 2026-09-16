using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Pages;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Helpers;

internal static class BrowseContexts
{
    public static BrowseContext From(object parameter)
    {
        if (parameter is BrowseNavigation nav) return nav.Context;
        if (parameter is GroupScopedSceneNavigationParameter group) return new(UiText.Get("Browser_AllImages"), group.Cards);
        AuthorKey? author = null;
        CardBase? card = parameter as CardBase;
        switch (parameter)
        {
            case AuthorScopedSceneNavigationParameter p: card = p.Card; author = p.AuthorKey; break;
            case AuthorScopedCharacterNavigationParameter p: card = p.Card; author = p.AuthorKey; break;
            case AuthorScopedCoordinateNavigationParameter p: card = p.Card; author = p.AuthorKey; break;
        }
        var libraries = App.Services.GetRequiredService<LibraryRegistry>();
        var adapter = card is null ? libraries.Get(LibraryKind.Scenes) : libraries.ForCard(card);
        IEnumerable<CardBase> cards = author is null ? adapter.ViewModel.CardsView.OfType<CardBase>() : adapter.Cards;
        if (author is not null) cards = cards.Where(c => (c as IAuthorOwner)?.Author?.Key == author);
        return new(author is null ? adapter.Title : (card as IAuthorOwner)?.Author?.Name ?? adapter.Title, cards);
    }
}

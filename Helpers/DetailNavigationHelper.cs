using CommunityToolkit.WinUI.Collections;
using KoikatsuSceneGallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace KoikatsuSceneGallery.Helpers;

internal static class DetailNavigationHelper
{
    public static (bool hasPrev, bool hasNext) GetNavigationState<TCard>(
        IList<TCard> cards,
        TCard? card)
        where TCard : CardBase
    {
        if (card is null || cards.Count == 0)
            return (false, false);

        var index = cards.IndexOf(card);
        return index < 0 ? (false, false) : (index > 0, index < cards.Count - 1);
    }

    public static (bool hasPrev, bool hasNext) GetNavigationState(AdvancedCollectionView view, object? card)
    {
        if (card == null || view.Count == 0)
            return (false, false);

        var index = view.IndexOf(card);
        if (index < 0) return (false, false);
        return (index > 0, index < view.Count - 1);
    }

    public static TCard? Navigate<TCard>(AdvancedCollectionView view, TCard? currentCard, int direction)
        where TCard : CardBase
    {
        if (currentCard == null) return null;

        var index = view.IndexOf(currentCard);
        var newIndex = index + direction;
        if (newIndex >= 0 && newIndex < view.Count && view[newIndex] is TCard card)
            return card;
        return null;
    }

    public static TCard? Navigate<TCard>(IList<TCard> cards, TCard? currentCard, int direction)
        where TCard : CardBase
    {
        if (currentCard is null) return null;

        var newIndex = cards.IndexOf(currentCard) + direction;
        return newIndex >= 0 && newIndex < cards.Count ? cards[newIndex] : null;
    }

    public static TCard? RandomCard<TCard>(AdvancedCollectionView view, TCard? currentCard)
        where TCard : CardBase
    {
        if (view.Count == 0) return null;

        var currentIndex = currentCard != null ? view.IndexOf(currentCard) : -1;
        var newIndex = Random.Shared.Next(view.Count);
        if (view.Count > 1)
        {
            while (newIndex == currentIndex)
                newIndex = Random.Shared.Next(view.Count);
        }

        return view[newIndex] as TCard;
    }

    public static TCard? RandomCard<TCard>(IList<TCard> cards, TCard? currentCard)
        where TCard : CardBase
    {
        if (cards.Count == 0) return null;

        var currentIndex = currentCard is null ? -1 : cards.IndexOf(currentCard);
        var newIndex = Random.Shared.Next(cards.Count);
        if (cards.Count > 1)
        {
            while (newIndex == currentIndex)
                newIndex = Random.Shared.Next(cards.Count);
        }

        return cards[newIndex];
    }

    public static TCard? FindAdjacentOnRemoval<TCard>(AdvancedCollectionView view, TCard card)
        where TCard : CardBase
    {
        var index = view.IndexOf(card);
        if (index >= 0 && index < view.Count - 1 && view[index + 1] is TCard next)
            return next;
        if (index > 0 && view[index - 1] is TCard prev)
            return prev;
        return null;
    }

    public static async Task HandleDragStartingAsync(CardBase? card, DragStartingEventArgs e)
    {
        if (card is null) return;
        var deferral = e.GetDeferral();
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(card.FilePath);
            e.Data.SetStorageItems([file]);
            e.Data.RequestedOperation = DataPackageOperation.Copy;
        }
        finally
        {
            deferral.Complete();
        }
    }

    public static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}

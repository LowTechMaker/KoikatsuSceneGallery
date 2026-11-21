using KoikatsuSceneGallery.Models;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Controls;

public sealed partial class LibraryBrowser
{
    // The menu itself is shared with the home page (CardMenu).
    private void CardMenu_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout menu || menu.Target?.DataContext is not GalleryEntry entry) return;
        CardMenu.Fill(menu, MenuTargets(entry), () => OpenEntry(entry), _frame);
    }
}

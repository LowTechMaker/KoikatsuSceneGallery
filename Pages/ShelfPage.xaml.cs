using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

/// <summary>What a home page panel's "see all" opens: a title and its cards.</summary>
internal sealed record ShelfPageParameter(string Title, IReadOnlyList<CardBase> Cards);

/// <summary>
/// A home page panel's whole list, in the library's own browser (select,
/// details pane, loupe, sort, search), scoped to the panel's cards.
/// </summary>
public sealed partial class ShelfPage : Page
{
    private bool _initialized;

    public ShelfPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (!_initialized && e.Parameter is ShelfPageParameter shelf)
        {
            _initialized = true;
            Header.Title = shelf.Title;
            Header.Subtitle = UiText.Format("Home_ShelfCount", shelf.Cards.Count.ToString("N0"));
            // The page header names the shelf; the browser's own heading would say it twice.
            Browser.ShowKindHeading = false;
            Browser.Initialize(LibraryKind.Scenes, shelf.Cards, shelf.Title);
        }
        Browser.Activate(Frame);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Browser.Deactivate();
        base.OnNavigatedFrom(e);
    }
}

using KoikatsuSceneGallery.Controls;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

/// <summary>
/// A card opened from somewhere with no grid behind it to open in place over:
/// random discovery, a post page, an author's overview, the command palette.
/// The same two pieces the library uses, the loupe and the details pane, as a
/// page; leaving the loupe (Esc, its close button) goes back.
/// </summary>
/// <remarks>
/// It replaces the scene, character and outfit detail pages (wave 15), which
/// were near copies of one another that had drifted apart. Every way of
/// opening a card (<see cref="BrowseContext.Open"/>, the author page's own
/// parameters) lands here.
/// </remarks>
public sealed class CardViewerPage : Page
{
    private readonly CardLoupe _loupe = new();
    private readonly CardInspector _inspector = new() { Width = 320, Margin = new Thickness(16, 0, 0, 0) };
    private BrowseContext? _context;
    private bool _leaving;

    public CardViewerPage()
    {
        var root = new Grid { Padding = (Thickness)Application.Current.Resources["DetailContentPadding"] };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(_loupe);
        Grid.SetColumn(_inspector, 1);
        root.Children.Add(_inspector);
        Content = root;

        _inspector.ShowPreview = false; // the loupe shows the picture
        _inspector.Frame = () => Frame;
        _inspector.CardRequested += card => _loupe.Replace(card);
        _inspector.CardGone += GoBack;
        _loupe.CurrentChanged += card =>
        {
            _inspector.Show([card]);
            // Back in the list it came from, that card is the one selected.
            if (_context is not null) _context.ReturnPath = card.FilePath;
        };
        _loupe.Closed += _ => { if (!_leaving) GoBack(); };

        // The loupe's keys and the card keys, wherever focus is; typing is left alone.
        root.PreviewKeyDown += (_, e) =>
        {
            if (e.OriginalSource is TextBox or AutoSuggestBox or PasswordBox) return;
            if (CardKeys.IsModifierDown()) return;
            if (_loupe.HandleKey(e.Key)) { e.Handled = true; return; }
            if (e.KeyStatus.WasKeyDown) return;
            if (CardKeys.Flag(e.Key) is { } flag) { e.Handled = true; _inspector.Toggle(flag); }
            else if (CardKeys.Rating(e.Key) is { } rating) { e.Handled = true; _inspector.RateFromKey(rating); }
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _leaving = false;
        var card = e.Parameter switch
        {
            BrowseNavigation navigation => navigation.Card,
            GroupScopedSceneNavigationParameter group => group.Card,
            AuthorScopedSceneNavigationParameter scoped => scoped.Card,
            AuthorScopedCharacterNavigationParameter scoped => scoped.Card,
            AuthorScopedCoordinateNavigationParameter scoped => (CardBase)scoped.Card,
            CardBase plain => plain,
            _ => null,
        };
        if (card is null)
        {
            GoBack();
            return;
        }
        _context = BrowseContexts.From(e.Parameter);
        // Neighbours as they are now: a card renamed or removed since is not offered.
        var cards = _context.CurrentCards().ToList();
        var index = cards.FindIndex(c => string.Equals(c.FilePath, card.FilePath, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            cards = [card];
            index = 0;
        }
        // The filmstrip asks for thumbnails the gallery may have cancelled when it was left.
        App.Services.GetRequiredService<LibraryRegistry>().ForCard(card).ViewModel.ActivateThumbnailRequests();
        _loupe.Open(cards, index);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _leaving = true;
        _loupe.Close(animate: false);
        base.OnNavigatedFrom(e);
    }

    private void GoBack()
    {
        _leaving = true;
        if (Frame?.CanGoBack == true) Frame.GoBack();
    }
}

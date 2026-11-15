using KoikatsuSceneGallery.Controls;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

/// <summary>
/// The start page: a few shelves that answer "which scene do I play now"
/// without searching, filtering or choosing a platform first.
/// </summary>
public sealed partial class HomePage : Page
{
    private readonly HomeViewModel _viewModel = App.Services.GetRequiredService<HomeViewModel>();
    private readonly CardStateService _states = App.Services.GetRequiredService<CardStateService>();
    private readonly GalleryViewModel _gallery = App.Services.GetRequiredService<GalleryViewModel>();
    private bool _refreshQueued;

    public HomePage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        foreach (var shelf in _viewModel.Shelves)
        {
            var row = new CardShelf(shelf);
            row.CardInvoked += OpenCard;
            row.FlagInvoked += ToggleFlag;
            if (shelf.Key == HomeViewModel.DealKey)
            {
                var again = new Button { Content = UiText.Get("Home_DealAgain") };
                AutomationProperties.SetAutomationId(again, "HomeDealAgain");
                again.Click += (_, _) => _viewModel.DealAgain();
                row.AddAction(again);
            }
            Shelves.Children.Add(row);
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _gallery.CardsReloaded += QueueRefresh;
        _states.StateChanged += State_Changed;
        // The shelves ask for thumbnails of cards the gallery may not have
        // shown, and leaving the gallery cancelled its thumbnail session.
        _gallery.ActivateThumbnailRequests();
        if (_gallery.Cards.Count == 0) _ = _gallery.LoadCardsCommand.ExecuteAsync(null);
        await _states.EnsureLoadedAsync();
        _viewModel.Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _gallery.CardsReloaded -= QueueRefresh;
        _states.StateChanged -= State_Changed;
        base.OnNavigatedFrom(e);
    }

    // Raised off the UI thread, once per card: a batch of ten changes should
    // rebuild the shelves once.
    private void State_Changed(string path) => QueueRefresh();

    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _refreshQueued = false;
            _viewModel.Refresh();
        });
    }

    private void OpenCard(HomeShelf shelf, CardBase card)
        => new BrowseContext(shelf.Title, shelf.Cards).Open(Frame, card);

    private async void ToggleFlag(CardBase card, CardFlag flag)
    {
        if (await _states.ToggleAsync(card.FilePath, flag) is null)
            AppToast.Show(UiText.Get("CardState_SaveFailed"), InfoBarSeverity.Error, key: "card-state");
    }
}

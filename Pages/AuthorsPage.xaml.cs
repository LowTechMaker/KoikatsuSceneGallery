using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class AuthorsPage : Page
{
    public AuthorsViewModel ViewModel { get; }

    public AuthorsPage()
    {
        ViewModel = App.Services.GetRequiredService<AuthorsViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Activate();
        App.Services.GetRequiredService<AuthorSourceCoordinator>()
            .EnsureLoadedAsync()
            .Observe(App.Services.GetRequiredService<IAppLogger>(), "Authors.EnsureLoaded");
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Deactivate();
        base.OnNavigatedFrom(e);
    }

    private void AuthorSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SearchText = sender.Text;
    }

    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
        {
            ViewModel.SortMode = item.Tag?.ToString() switch
            {
                "Name" => AuthorSortMode.Name,
                "LastUpdated" => AuthorSortMode.LastUpdated,
                _ => AuthorSortMode.Count,
            };
        }
    }

    private void AuthorsPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        QuickJumpList.ItemsSource = AuthorsPivot.SelectedItem is AuthorProviderTabViewModel tab
            ? tab.QuickJumpItems
            : null;
    }

    private void JumpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AuthorGroupViewModel group })
            JumpToGroup(group);
    }

    private void AuthorsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AuthorSummary summary)
            OpenAuthorDetail(summary);
    }

    private void OpenProfile_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Authors.OpenProfile", async () =>
        {
            if (sender is FrameworkElement { Tag: AuthorSummary summary })
                await Windows.System.Launcher.LaunchUriAsync(new Uri(summary.Display.ProfileUrl));
        });

    private void OpenAuthorDetail(AuthorSummary summary)
        => Frame.Navigate(typeof(AuthorDetailPage), new AuthorDetailNavigationParameter(summary));

    private void RefreshOne_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Authors.RefreshOne", async () =>
        {
            if (sender is FrameworkElement { Tag: AuthorSummary summary })
                await ViewModel.RefreshOneAsync(summary);
        });

    private void JumpToGroup(AuthorGroupViewModel group)
    {
        var firstAuthor = group.Authors.FirstOrDefault();
        if (firstAuthor is null)
            return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (AuthorsPivot.SelectedItem is not AuthorProviderTabViewModel tab)
                return;

            if (AuthorsPivot.ContainerFromItem(tab) is DependencyObject container
                && VisualTreeSearch.FindDescendantByName<GridView>(container, "AuthorsGrid") is { } gridView)
            {
                gridView.ScrollIntoView(firstAuthor, ScrollIntoViewAlignment.Leading);
            }
        });
    }

}

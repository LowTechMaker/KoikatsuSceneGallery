using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class AuthorsPage : Page
{
    public AuthorsViewModel ViewModel { get; }

    public AuthorsPage()
    {
        ViewModel = App.AuthorsViewModel;
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.RefreshAuthorSources();
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

    private async void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AuthorSummary summary })
            await Windows.System.Launcher.LaunchUriAsync(new Uri(summary.Display.ProfileUrl));
    }

    private void OpenAuthorDetail(AuthorSummary summary)
        => Frame.Navigate(typeof(AuthorDetailPage), new AuthorDetailNavigationParameter(summary));

    private async void RefreshOne_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AuthorSummary summary })
            await ViewModel.RefreshOneAsync(summary);
    }

    private void JumpToGroup(AuthorGroupViewModel group)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (AuthorsPivot.SelectedItem is not AuthorProviderTabViewModel tab)
                return;

            if (AuthorsPivot.ContainerFromItem(tab) is DependencyObject container
                && FindDescendantByName<ListView>(container, "GroupListView") is { } listView)
            {
                listView.ScrollIntoView(group, ScrollIntoViewAlignment.Leading);
            }
        });
    }

    private static T? FindDescendantByName<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T element && element.Name == name)
                return element;

            if (FindDescendantByName<T>(child, name) is { } descendant)
                return descendant;
        }

        return null;
    }
}

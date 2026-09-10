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
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _searchTimer;

    public AuthorsPage()
    {
        ViewModel = App.Services.GetRequiredService<AuthorsViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _searchTimer = DispatcherQueue.CreateTimer();
        _searchTimer.Interval = TimeSpan.FromMilliseconds(200); _searchTimer.IsRepeating = false;
        _searchTimer.Tick += (_, _) => ViewModel.SearchText = AuthorSearchBox.Text;
        var find = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.F, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        find.Invoked += (_, e) => { AuthorSearchBox.Focus(FocusState.Programmatic); e.Handled = true; };
        KeyboardAccelerators.Add(find);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // AuthorSourceCoordinator already attaches to the gallery collections at
        // startup, and folder-setting changes explicitly trigger a rebuild. Do not
        // rebuild every card here: it synchronously clears and reassigns authors
        // for the whole library and makes opening this page hitch.
        App.Services.GetRequiredService<AuthorSourceCoordinator>()
            .EnsureLoadedAsync()
            .Observe(App.Services.GetRequiredService<IAppLogger>(), "Authors.EnsureLoaded");
    }

    private void AuthorSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _searchTimer?.Stop(); _searchTimer?.Start();
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

    // The grid lives in a Pivot item template, so it is reached through the
    // sender rather than by name.
    private void AuthorsGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is GridView grid)
            AuthorTileLayout.Apply(grid);
    }

    private void AuthorsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is GridView grid)
            AuthorTileLayout.Apply(grid);
    }

    private void AuthorsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AuthorSummary summary)
            OpenAuthorDetail(summary);
    }

    private void OpenProfile_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Authors.OpenProfile", async () =>
        {
            // A local source has no profile, and the menu item is hidden for
            // one; guarded here as well so no path can launch an empty Uri.
            if (sender is FrameworkElement { Tag: AuthorSummary summary }
                && summary.Display.HasProfileUrl)
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(summary.Display.ProfileUrl));
            }
        });

    private void OpenAuthorDetail(AuthorSummary summary)
        => Frame.Navigate(typeof(AuthorDetailPage), new AuthorDetailNavigationParameter(summary));

    private void EditLocal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AuthorSummary summary })
            return;

        UiEventGuard.Run(
            App.Services.GetRequiredService<IAppLogger>(),
            "Authors.EditLocal",
            () => LocalSourceEditing.RunAsync(XamlRoot, summary.Display.Key.Id));
    }

    private void RefreshOne_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Authors.RefreshOne", async () =>
        {
            if (sender is FrameworkElement { Tag: AuthorSummary summary })
                await ViewModel.RefreshOneAsync(summary);
        });

    private void ClearSearch_Click(object sender, RoutedEventArgs e) => AuthorSearchBox.Text = string.Empty;

    private void JumpToGroup(AuthorGroupViewModel group)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (AuthorsPivot.SelectedItem is not AuthorProviderTabViewModel tab)
                return;

            if (AuthorsPivot.ContainerFromItem(tab) is DependencyObject container
                && VisualTreeSearch.FindDescendantByName<GridView>(container, "AuthorsGrid") is { } listView)
            {
                if (group.Authors.FirstOrDefault() is { } first) listView.ScrollIntoView(first, ScrollIntoViewAlignment.Leading);
            }
        });
    }

}

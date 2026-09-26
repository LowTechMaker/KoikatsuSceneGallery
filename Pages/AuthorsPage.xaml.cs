using System.Collections.Specialized;
using System.ComponentModel;
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
    private AuthorProviderTabViewModel? _currentTab;
    private bool _syncing;
    private readonly List<AuthorProviderTabViewModel> _watched = [];

    public AuthorsPage()
    {
        ViewModel = App.Services.GetRequiredService<AuthorsViewModel>();
        // Switching between two platforms that both have authors leaves this
        // page open, so the picker has to follow rather than wait for the next
        // navigation.
        App.Services.GetRequiredService<SettingsViewModel>().BrowseOriginChanged +=
            _ => DispatcherQueue.TryEnqueue(ApplySelectedPlatform);
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        // The tabs arrive once the providers are known, which is after this
        // page may already be on screen.
        ((INotifyCollectionChanged)ViewModel.ProviderTabs).CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => { RebuildPlatformBar(); ApplySelectedPlatform(); });
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
        RebuildPlatformBar();
        ApplySelectedPlatform();
        SyncSortUi();
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

    private void SortField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        ViewModel.SortMode = tag switch
        {
            "Name" => AuthorSortMode.Name,
            "LastUpdated" => AuthorSortMode.LastUpdated,
            _ => AuthorSortMode.Count,
        };
        SyncSortUi();
    }

    /// <summary>
    /// Puts the chosen field on the button's face and the tick beside it.
    /// </summary>
    private void SyncSortUi()
    {
        var chosen = ViewModel.SortMode switch
        {
            AuthorSortMode.Name => SortByName,
            AuthorSortMode.LastUpdated => SortByLastUpdated,
            _ => SortByCount,
        };
        SortByCount.IsChecked = ReferenceEquals(chosen, SortByCount);
        SortByLastUpdated.IsChecked = ReferenceEquals(chosen, SortByLastUpdated);
        SortByName.IsChecked = ReferenceEquals(chosen, SortByName);
        SortLabel.Text = chosen.Text;
    }

    /// <summary>
    /// Makes the picker hold one item per platform tab.
    /// </summary>
    /// <remarks>
    /// Runs on every visit, but the tabs are fixed once the providers are
    /// known, so almost always there is nothing to rebuild and only the labels
    /// are refreshed. Clearing and refilling on each visit is what crashed the
    /// app on a click of the sidebar: the items were replaced while the bar was
    /// off screen, its selection still named an item no longer in the list, and
    /// the next measure failed natively ("SelectedItem must be an element of
    /// Items") — a fail-fast no handler can catch, as in phase-88.
    ///
    /// When the tabs really did change and the bar is not on screen, the whole
    /// rebuild waits for it, and then clears the selection, replaces the items
    /// and selects again in one go, so no layout pass ever sees them disagree.
    /// </remarks>
    private void RebuildPlatformBar()
    {
        if (PlatformBar.Items.Select(item => item.Tag).SequenceEqual(ViewModel.ProviderTabs))
        {
            foreach (var item in PlatformBar.Items)
            {
                if (item.Tag is AuthorProviderTabViewModel tab)
                    item.Text = LabelFor(tab);
            }
            return;
        }

        if (!PlatformBar.IsLoaded)
        {
            _platformBarRebuildPending = true;
            PlatformBar.Loaded -= PlatformBar_ApplyPending;
            PlatformBar.Loaded += PlatformBar_ApplyPending;
            return;
        }

        _syncing = true;
        foreach (var tab in _watched) tab.PropertyChanged -= Tab_PropertyChanged;
        _watched.Clear();
        PlatformBar.SelectedItem = null;
        PlatformBar.Items.Clear();
        foreach (var tab in ViewModel.ProviderTabs)
        {
            // FontSize on the bar does not reach the items — the container's
            // template sizes its own text — so it is set per item. 24 SemiLight
            // is what the pivot headers used to read at (PivotHeaderItemFontSize
            // and PivotHeaderItemThemeFontWeight in WinUI's generic.xaml).
            PlatformBar.Items.Add(new SelectorBarItem
            {
                Text = LabelFor(tab),
                Tag = tab,
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiLight,
            });
            tab.PropertyChanged += Tab_PropertyChanged;
            _watched.Add(tab);
        }
        PlatformBar.SelectedItem = PlatformBar.Items.FirstOrDefault(item => ReferenceEquals(item.Tag, _currentTab));
        _syncing = false;
    }

    private bool _platformBarRebuildPending;

    /// <summary>
    /// A platform's name and how many authors it holds, as the pivot headers
    /// used to read.
    /// </summary>
    /// <remarks>
    /// PlatformDisplayName, not the provider's own DisplayName: the title bar
    /// spells these from the same resource, and "Fanbox" beside "FANBOX" reads
    /// as two different things.
    /// </remarks>
    private static string LabelFor(AuthorProviderTabViewModel tab)
    {
        var name = PlatformDisplayName.For(tab.ProviderId);
        return tab.CountText.Length > 0 ? $"{name} {tab.CountText}" : name;
    }

    /// <summary>
    /// Follows the platform the title bar names.
    /// </summary>
    /// <remarks>
    /// While a platform is named there is no choice left for this page to offer,
    /// so the picker is not shown at all — it used to remain as a one-item tab
    /// strip, which announced a choice that did not exist and repeated the title
    /// bar's answer in larger type. Under "all" the title bar names nothing, so
    /// the picker comes back.
    /// </remarks>
    private void ApplySelectedPlatform()
    {
        var selected = App.Services.GetRequiredService<SettingsViewModel>().EffectiveBrowseOrigin;
        var named = !selected.IsAll;

        var tab = named
            ? ViewModel.ProviderTabs.FirstOrDefault(candidate =>
                string.Equals(candidate.ProviderId, selected.ProviderId, StringComparison.OrdinalIgnoreCase))
            : _currentTab;

        PlatformBar.Visibility = !named && ViewModel.ProviderTabs.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;

        ShowTab(tab ?? ViewModel.ProviderTabs.FirstOrDefault());
        // ShowTab returns early when the platform has not changed, and the
        // picker's visibility decides where the count belongs.
        UpdateCount();
    }

    private void ShowTab(AuthorProviderTabViewModel? tab)
    {
        if (ReferenceEquals(tab, _currentTab) && TabHost.Content is not null)
        {
            SyncPlatformSelection();
            return;
        }

        _currentTab = tab;
        TabHost.Content = tab;
        QuickJumpList.ItemsSource = tab?.QuickJumpItems;
        UpdateCount();
        SyncPlatformSelection();
    }

    /// <summary>
    /// Marks the current platform in the picker, once the picker can take it.
    /// </summary>
    /// <remarks>
    /// <see cref="OnNavigatedTo"/> runs before the page is attached, and this
    /// page has already been through one fail-fast caused by assigning a
    /// selection to a ListViewBase-derived control that had not applied its
    /// template yet: the native measure that followed returned E_FAIL, which
    /// XAML stows and turns into a crash no managed handler can catch. Waiting
    /// for Loaded costs nothing and closes the whole class (phase-88).
    /// </remarks>
    private void SyncPlatformSelection()
    {
        if (!PlatformBar.IsLoaded)
        {
            PlatformBar.Loaded -= PlatformBar_ApplyPending;
            PlatformBar.Loaded += PlatformBar_ApplyPending;
            return;
        }

        _syncing = true;
        PlatformBar.SelectedItem = PlatformBar.Items
            .FirstOrDefault(item => ReferenceEquals(item.Tag, _currentTab));
        _syncing = false;
    }

    private void PlatformBar_ApplyPending(object sender, RoutedEventArgs e)
    {
        PlatformBar.Loaded -= PlatformBar_ApplyPending;
        if (_platformBarRebuildPending)
        {
            _platformBarRebuildPending = false;
            RebuildPlatformBar();
        }
        SyncPlatformSelection();
    }

    private void PlatformBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_syncing) return;
        if (sender.SelectedItem is { Tag: AuthorProviderTabViewModel tab })
            ShowTab(tab);
    }

    private void Tab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(AuthorProviderTabViewModel.AuthorCount)) return;
        if (sender is AuthorProviderTabViewModel tab
            && PlatformBar.Items.FirstOrDefault(i => ReferenceEquals(i.Tag, tab)) is { } item)
        {
            item.Text = LabelFor(tab);
        }
        UpdateCount();
    }

    /// <summary>
    /// The count beside the sort control, unless the picker is already carrying
    /// it: each platform's tab states its own total, the way the pivot headers
    /// did, so repeating it here would say the same number twice on one screen.
    /// </summary>
    private void UpdateCount()
        => AuthorCount.Text = PlatformBar.Visibility == Visibility.Visible
            ? string.Empty
            : _currentTab is { AuthorCount: > 0 } tab
                ? UiText.Format("Authors_TotalCount", tab.AuthorCount)
                : string.Empty;

    private void JumpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AuthorGroupViewModel group })
            JumpToGroup(group);
    }

    // The grid lives in a data template, so it is reached through the sender
    // rather than by name.
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
            if (VisualTreeSearch.FindDescendantByName<GridView>(TabHost, "AuthorsGrid") is { } listView
                && group.Authors.FirstOrDefault() is { } first)
            {
                listView.ScrollIntoView(first, ScrollIntoViewAlignment.Leading);
            }
        });
    }

}

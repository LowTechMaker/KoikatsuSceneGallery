using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using CommunityToolkit.WinUI.Controls;
using KoikatsuSceneGallery.Controls;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Pages;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery;

public sealed partial class MainWindow : Window
{
    private bool _suppressLibrarySelectionChanged;
    private bool _suppressOriginSelectionChanged;
    private bool _originSelectorAvailable;

    // The title and window buttons also need room beside the platform choices.
    private const double CompactOriginSelectorWidth = 900;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");


        if (App.Services.GetService<ImportViewModel>() is { } importViewModel)
        {
            ImportNavItem.Visibility = Visibility.Visible;
            importViewModel.PropertyChanged += ImportViewModel_PropertyChanged;
            UpdateImportNavBadge();
        }

        if (App.Services.GetService<ArtworkRefetchService>() is { } refetch)
        {
            refetch.PropertyChanged += (_, _) => UpdateRefetchBanner(refetch);
            UpdateRefetchBanner(refetch);
        }

        var settings = App.Services.GetRequiredService<SettingsViewModel>();

        // Online versus local only means something while an online platform is
        // installed. With none, every card is "not local" and the switch is a
        // control that cannot change anything. The stored choice is left as it
        // is — only its application is suppressed — so it returns intact when a
        // plugin comes back.
        var installed = App.Services.GetRequiredService<TagCloudService>().InstalledProviderIds();
        var platforms = PlatformAvailability.Online(installed);
        var hasPlatform = platforms.Count > 0;
        settings.PlatformFilteringAvailable = hasPlatform;
        _originSelectorAvailable = hasPlatform;
        BuildOriginSelector(platforms);
        UpdateOriginSelectorLayout();

        UpdateLibrarySelectorEmphasis();
        SelectOriginItem(settings.EffectiveBrowseOrigin);
        ApplyBrowseOrigin(settings.EffectiveBrowseOrigin);
        PlatformAccent.Apply(settings.EffectiveBrowseOrigin, Content as FrameworkElement);
        ApplyNavVisibility();
        settings.NavItemVisibilityChanged += OnNavItemVisibilityChanged;
        // Which platforms carry tags is a fact about the sidecars, so it takes a
        // scan to know. Start it now rather than on the first visit: until it
        // finishes the tag entry is offered for every platform, and a platform
        // that turns out to have none would otherwise keep offering an empty
        // page for the whole session.
        var tagCloud = App.Services.GetRequiredService<TagCloudService>();
        tagCloud.CloudChanged += OnTagCloudChanged;
        WarmTagCloudWhenCardsArrive(tagCloud);
        // The author tabs are built from the installed providers, but not until
        // the author scan runs, so the entry would otherwise stay hidden from
        // startup until something else happened to recompute visibility.
        App.Services.GetRequiredService<AuthorsViewModel>().ProviderTabs.CollectionChanged +=
            (_, _) => DispatcherQueue.TryEnqueue(ApplyNavVisibility);
        settings.BrowseOriginChanged += OnBrowseOriginChanged;
        RecordModeNavToggle.IsOn = settings.RecordModeEnabled;
        settings.RecordModeEnabledChanged += OnRecordModeEnabledChanged;

        NavigateToSelectedLibraryPage();
    }

    private void ImportViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.ImportViewModel.IsImporting)
            or nameof(ViewModels.ImportViewModel.IsAnalyzing)
            or nameof(ViewModels.ImportViewModel.AnalysisPendingCount)
            or nameof(ViewModels.ImportViewModel.AnalysisStatusText))
        {
            UpdateImportNavBadge();
        }
    }

    // The phase the user closed the banner in. Closing hides it for the rest
    // of that phase only: a run that then stops or finishes still says so.
    private ArtworkRefetchPhase? _refetchBannerDismissedIn;
    private bool _refetchBannerWired;

    private void UpdateRefetchBanner(ArtworkRefetchService refetch)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdateRefetchBanner(refetch));
            return;
        }

        if (!_refetchBannerWired)
        {
            _refetchBannerWired = true;
            RefetchBanner.CloseButtonClick += (_, _) => _refetchBannerDismissedIn = refetch.Phase;
            // The close button closes the InfoBar itself; its host would stay
            // behind as an empty card.
            RefetchBanner.Closed += (_, _) => RefetchBannerHost.Visibility = Visibility.Collapsed;
        }

        var platform = refetch.ProviderId is { } id ? PlatformDisplayName.For(id) : "";
        RefetchBanner.Title = UiText.Format("Refetch_BannerTitle", platform);
        var (show, severity, message) = refetch.Phase switch
        {
            ArtworkRefetchPhase.Scanning => (true, InfoBarSeverity.Informational, UiText.Get("Refetch_Scanning")),
            ArtworkRefetchPhase.BackingUp => (true, InfoBarSeverity.Informational, UiText.Get("Refetch_BackingUp")),
            ArtworkRefetchPhase.Restoring => (true, InfoBarSeverity.Informational, UiText.Get("Refetch_Restoring")),
            ArtworkRefetchPhase.Running => (true, InfoBarSeverity.Informational,
                UiText.Format("Refetch_Progress", refetch.RunDone, refetch.RunTotal, refetch.Updated, refetch.Gone, refetch.Failed)),
            ArtworkRefetchPhase.Completed => (true, InfoBarSeverity.Success,
                UiText.Format("Refetch_Completed", refetch.Updated, refetch.Gone, refetch.Failed)),
            ArtworkRefetchPhase.Stopped when refetch.StopReason == ArtworkRefetchStopReason.TooManyFailures =>
                (true, InfoBarSeverity.Warning,
                    UiText.Format("Refetch_TooManyFailures", ArtworkRefetchPolicy.MaxConsecutiveFailures)),
            ArtworkRefetchPhase.Stopped when refetch.StopReason == ArtworkRefetchStopReason.BackupFailed =>
                (true, InfoBarSeverity.Error, UiText.Get("Refetch_BackupFailed")),
            ArtworkRefetchPhase.Stopped when refetch.StopReason == ArtworkRefetchStopReason.Error =>
                (true, InfoBarSeverity.Error, UiText.Get("Refetch_Error")),
            _ => (false, InfoBarSeverity.Informational, ""),
        };

        if (_refetchBannerDismissedIn is { } dismissed && dismissed != refetch.Phase)
            _refetchBannerDismissedIn = null;

        RefetchBanner.Severity = severity;
        RefetchBanner.Message = message;
        RefetchBannerStop.Visibility = refetch.IsBusy && refetch.Phase != ArtworkRefetchPhase.Restoring
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefetchBannerProgress.Visibility = refetch.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        RefetchBannerProgress.IsIndeterminate = refetch.Phase != ArtworkRefetchPhase.Running;
        RefetchBannerProgress.Maximum = Math.Max(1, refetch.RunTotal);
        RefetchBannerProgress.Value = refetch.RunDone;
        var open = show && _refetchBannerDismissedIn is null;
        RefetchBanner.IsOpen = open;
        RefetchBannerHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefetchBannerStop_Click(object sender, RoutedEventArgs e)
        => App.Services.GetService<ArtworkRefetchService>()?.Stop();

    private void UpdateImportNavBadge()
    {
        if (App.Services.GetService<ImportViewModel>() is not { } viewModel)
            return;

        if (viewModel.IsImporting)
        {
            ImportNavItem.InfoBadge = CreateImportStatusBadge();
            ToolTipService.SetToolTip(ImportNavItem, "Importing");
            return;
        }

        if (viewModel.IsAnalyzing)
        {
            var pendingCount = viewModel.AnalysisPendingCount;
            ImportNavItem.InfoBadge = pendingCount > 0
                ? CreateImportStatusBadge(pendingCount)
                : CreateImportStatusBadge();
            ToolTipService.SetToolTip(ImportNavItem, $"Analyzing {pendingCount} pending");
            return;
        }

        ImportNavItem.InfoBadge = null;
        ToolTipService.SetToolTip(ImportNavItem, null);
    }

    private static InfoBadge CreateImportStatusBadge(int? value = null)
    {
        var badge = new InfoBadge
        {
            Style = (Style)Application.Current.Resources["AttentionIconInfoBadgeStyle"],
        };

        if (value is not null)
            badge.Value = value.Value;

        return badge;
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack)
            NavFrame.GoBack();
    }

    private void ApplyNavVisibility()
    {
        var vm = App.Services.GetRequiredService<SettingsViewModel>();
        SetNavItemVisibility("gallery", vm.ShowGalleryNav);
        SetNavItemVisibility("characters", vm.ShowCharactersNav);
        SetNavItemVisibility("coordinates", vm.ShowCoordinatesNav);

        // The two source-specific entries answer to the browse origin instead of
        // to HiddenNavItems, which only ever covers the four library entries —
        // so the two policies never contend for the same item.
        // Not AuthorInfoService.IsAvailable: the built-in local provider makes
        // that permanently true, and this page no longer shows local sources.
        // Both pages show one platform's data, so each is offered only while the
        // selected platform is one that has that data. Authors go by capability
        // — a plugin that resolves authors, whether or not any were found yet —
        // while tags exist only as sidecars on disk, so they go by what is
        // actually there.
        var authors = App.Services.GetRequiredService<AuthorsViewModel>().ProvidersWithAuthors;
        SetNavItemVisibility("authors", CardOriginNavPolicy.ShowAuthors(vm.EffectiveBrowseOrigin, authors));
        SetNavItemVisibility("localsources", CardOriginNavPolicy.ShowLocalSources(vm.EffectiveBrowseOrigin));
        var tags = App.Services.GetRequiredService<TagCloudService>().PlatformsOfferingTags;
        SetNavItemVisibility("tags", CardOriginNavPolicy.ShowTags(vm.EffectiveBrowseOrigin, tags));
    }

    private void OnNavItemVisibilityChanged(string tag, bool visible)
    {
        DispatcherQueue.TryEnqueue(() => SetNavItemVisibility(tag, visible));
    }

    private void SetNavItemVisibility(string tag, bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        switch (tag)
        {
            case "gallery":
                ScenesSelectorItem.Visibility = visibility;
                UpdateLibraryNavVisibility();
                break;
            case "characters":
                CharactersSelectorItem.Visibility = visibility;
                UpdateLibraryNavVisibility();
                break;
            case "coordinates":
                CoordinatesSelectorItem.Visibility = visibility;
                UpdateLibraryNavVisibility();
                break;
            case "authors":
                AuthorsNavItem.Visibility = visibility;
                break;
            case "localsources":
                LocalSourcesNavItem.Visibility = visibility;
                break;
            case "tags":
                TagsNavItem.Visibility = visibility;
                break;
        }
    }

    private void OnRecordModeEnabledChanged(bool enabled)
        => DispatcherQueue.TryEnqueue(() =>
        {
            RecordModeNavToggle.IsOn = enabled;
        });

    /// <summary>
    /// Keeps a tap on the switch from also selecting the row. Without this the
    /// app navigates to the log every time recording is turned on or off.
    /// </summary>
    private void RecordModeNavToggle_PointerPressed(object sender, PointerRoutedEventArgs e)
        => e.Handled = true;

    private void RecordModeNavToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var settings = App.Services.GetRequiredService<SettingsViewModel>();
        if (settings.RecordModeEnabled != RecordModeNavToggle.IsOn)
            settings.RecordModeEnabled = RecordModeNavToggle.IsOn;
    }

    /// <summary>
    /// One item per installed platform, plus All and local.
    /// </summary>
    /// <remarks>
    /// Local is offered unconditionally: it is the built-in provider, its cards
    /// exist whether or not any plugin does, and it has a page of its own. The
    /// remote platforms come from the plugins actually loaded, so a platform
    /// whose plugin was removed stops being offered — the same rule the tag
    /// page follows.
    /// </remarks>
    private void BuildOriginSelector(IReadOnlyList<string> platforms)
    {
        _suppressOriginSelectionChanged = true;
        OriginSelectorBar.Items.Clear();
        OriginSelectorMenu.Items.Clear();
        AddOriginItem(CardOriginSelection.All, UiText.Get("Platform_All"), "OriginAllItem");
        foreach (var providerId in platforms)
        {
            AddOriginItem(
                CardOriginSelection.For(providerId),
                PlatformDisplayName.For(providerId),
                $"OriginItem_{providerId}");
        }
        AddOriginItem(
            CardOriginSelection.Local,
            PlatformDisplayName.For(LocalSourceIdentity.ProviderId),
            "OriginItem_local");
        _suppressOriginSelectionChanged = false;
    }

    private void AddOriginItem(CardOriginSelection selection, string label, string automationId)
    {
        var tag = selection.ToConfigValue();
        var segment = new SegmentedItem { Content = label, Tag = tag };
        AutomationProperties.SetAutomationId(segment, automationId);
        AutomationProperties.SetName(segment, label);
        OriginSelectorBar.Items.Add(segment);

        var option = new RadioMenuFlyoutItem
        {
            Text = label,
            Tag = tag,
            GroupName = "origin",
        };
        AutomationProperties.SetAutomationId(option, $"{automationId}Picker");
        AutomationProperties.SetName(option, label);
        option.Click += OriginSelectorPicker_Click;
        OriginSelectorMenu.Items.Add(option);
    }

    private void RootLayout_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateOriginSelectorLayout();

    private void UpdateOriginSelectorLayout()
    {
        if (OriginSelectorBar is null || OriginSelectorPicker is null) return;

        var compact = RootLayout.ActualWidth > 0
            && RootLayout.ActualWidth < CompactOriginSelectorWidth;
        OriginSelectorBar.Visibility = _originSelectorAvailable && !compact
            ? Visibility.Visible : Visibility.Collapsed;
        OriginSelectorPicker.Visibility = _originSelectorAvailable && compact
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Builds the tag cloud once the library has something in it.
    /// </summary>
    /// <remarks>
    /// Not at startup: the author folders it reads are derived from the paths
    /// of the cards in memory, and at that point nothing has been scanned, so
    /// it would find nothing and answer "no platform has tags" — hiding the
    /// page for the session.
    /// </remarks>
    private void WarmTagCloudWhenCardsArrive(TagCloudService tagCloud)
    {
        if (!tagCloud.HasAnyPlatform || tagCloud.Cloud is not null) return;

        // CardsReloaded, not the collection changing: the scan publishes in
        // batches, so the collection is non-empty long before it is complete.
        // Building then reads only the author folders of whatever arrived first
        // and records the platforms missing from that batch as having no tags.
        var scenes = App.Services.GetRequiredService<LibraryRegistry>().Get(LibraryKind.Scenes);
        void OnReloaded()
        {
            if (tagCloud.Cloud is not null) return;
            scenes.ViewModel.CardsReloaded -= OnReloaded;
            tagCloud.BuildAsync()
                .Observe(App.Services.GetRequiredService<IAppLogger>(), "MainWindow.WarmTagCloud");
        }

        if (!scenes.ViewModel.IsLoading && scenes.Cards.Count > 0) OnReloaded();
        else scenes.ViewModel.CardsReloaded += OnReloaded;
    }

    private void OnTagCloudChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        ApplyNavVisibility();
        LeaveHiddenPage();
    });

    private void OnBrowseOriginChanged(CardOriginSelection origin)
    {
        var effective = App.Services.GetRequiredService<SettingsViewModel>().EffectiveBrowseOrigin;
        DispatcherQueue.TryEnqueue(() =>
        {
            SelectOriginItem(effective);
            ApplyBrowseOrigin(effective);
            PlatformAccent.Apply(effective, Content as FrameworkElement);
            ApplyNavVisibility();
            LeaveHiddenPage();
        });
    }

    /// <summary>
    /// Pushes the mode into every gallery. Each view model already rebuilds its
    /// shuffle queue and re-filters when the value lands.
    /// </summary>
    private static void ApplyBrowseOrigin(CardOriginSelection origin)
    {
        foreach (var library in App.Services.GetRequiredService<LibraryRegistry>().All)
            library.ViewModel.OriginFilter = origin;
    }

    /// <summary>
    /// Leaves a page the switch just hid, so the user is never stranded on a
    /// page with no matching entry in the pane.
    /// </summary>
    private void LeaveHiddenPage()
    {
        var current = NavFrame?.CurrentSourcePageType;
        var stranded =
            (current == typeof(AuthorsPage) && AuthorsNavItem.Visibility != Visibility.Visible)
            || (current == typeof(LocalSourcesPage) && LocalSourcesNavItem.Visibility != Visibility.Visible)
            || (current == typeof(TagCloudPage) && TagsNavItem.Visibility != Visibility.Visible);
        if (stranded) NavigateToSelectedLibraryPage();
    }

    /// <summary>
    /// Highlights the stored platform, falling back to All when its plugin is
    /// no longer installed.
    /// </summary>
    /// <remarks>
    /// The fallback is display only. The stored value is left untouched so that
    /// reinstalling the plugin brings the user's own choice back, which is the
    /// same reason <see cref="SettingsViewModel.EffectiveBrowseOrigin"/> does
    /// not write either.
    /// </remarks>
    private void SelectOriginItem(CardOriginSelection origin)
    {
        _suppressOriginSelectionChanged = true;
        var wanted = origin.ToConfigValue();
        OriginSelectorBar.SelectedItem = OriginSelectorBar.Items
            .OfType<FrameworkElement>()
            .FirstOrDefault(item => item.Tag as string == wanted)
            ?? OriginSelectorBar.Items.OfType<FrameworkElement>().FirstOrDefault();
        var options = OriginSelectorMenu.Items.OfType<RadioMenuFlyoutItem>().ToArray();
        var picked = options.FirstOrDefault(item => item.Tag as string == wanted)
            ?? options.FirstOrDefault();
        foreach (var option in options)
            option.IsChecked = ReferenceEquals(option, picked);
        OriginSelectorPicker.Content = picked?.Text;
        if (picked is not null)
            AutomationProperties.SetName(OriginSelectorPicker,
                $"{UiText.Get("Origin_SelectorBar.AutomationProperties.Name")}: {picked.Text}");
        _suppressOriginSelectionChanged = false;
    }

    private void OriginSelectorBar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressOriginSelectionChanged) return;
        if ((sender as Segmented)?.SelectedItem is not FrameworkElement { Tag: string tag }) return;
        App.Services.GetRequiredService<SettingsViewModel>().BrowseOrigin =
            CardOriginSelection.Parse(tag);
    }

    private void OriginSelectorPicker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: string tag }) return;
        var settings = App.Services.GetRequiredService<SettingsViewModel>();
        settings.BrowseOrigin = CardOriginSelection.Parse(tag);
        SelectOriginItem(settings.EffectiveBrowseOrigin);
    }

    private void UpdateLibraryNavVisibility()
    {
        var visibleItems = new[]
        {
            ScenesSelectorItem,
            CharactersSelectorItem,
            CoordinatesSelectorItem,
        }.Where(item => item.Visibility == Visibility.Visible).ToList();

        LibraryNavItem.Visibility = visibleItems.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (LibrarySelectorBar.SelectedItem is SelectorBarItem selected
            && selected.Visibility == Visibility.Visible)
        {
            return;
        }

        var next = visibleItems.FirstOrDefault() ?? ScenesSelectorItem;
        _suppressLibrarySelectionChanged = true;
        LibrarySelectorBar.SelectedItem = next;
        _suppressLibrarySelectionChanged = false;

        if (NavFrame is not null
            && ReferenceEquals(NavView.SelectedItem, LibraryNavItem)
            && IsLibraryPage(NavFrame.CurrentSourcePageType))
        {
            NavigateToSelectedLibraryPage(replaceCurrentLibraryPage: true);
        }
    }

    private void LibrarySelectorBar_SelectionChanged(
        SelectorBar sender,
        SelectorBarSelectionChangedEventArgs args)
    {
        if (_suppressLibrarySelectionChanged
            || NavFrame is null
            || !ReferenceEquals(NavView.SelectedItem, LibraryNavItem))
        {
            return;
        }

        NavigateToSelectedLibraryPage(replaceCurrentLibraryPage: true);
    }

    private void NavigateToSelectedLibraryPage(bool replaceCurrentLibraryPage = false)
    {
        var pageType = LibrarySelectorBar.SelectedItem switch
        {
            var item when item == CharactersSelectorItem => typeof(CharacterGalleryPage),
            var item when item == CoordinatesSelectorItem => typeof(CoordinateGalleryPage),
            _ => typeof(GalleryPage),
        };

        var previousPageType = NavFrame.CurrentSourcePageType;
        if (previousPageType == pageType)
            return;

        if (!NavFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo()))
            return;

        if (replaceCurrentLibraryPage
            && IsLibraryPage(previousPageType)
            && NavFrame.BackStack.Count > 0)
        {
            NavFrame.BackStack.RemoveAt(NavFrame.BackStack.Count - 1);
        }
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        if (e.SourcePageType == typeof(RandomDiscoveryPage))
            NavView.SelectedItem = DiscoveryNavItem;
        // Both pages navigate to AuthorDetailPage, so returning from one has to
        // restore the item the user actually came from.
        else if (e.SourcePageType == typeof(LocalSourcesPage))
            NavView.SelectedItem = LocalSourcesNavItem;
        // The tag cloud hosts a scoped browser that navigates on to a detail
        // page, so coming back has to reselect this entry.
        else if (e.SourcePageType == typeof(TagCloudPage))
            NavView.SelectedItem = TagsNavItem;
        else if (e.SourcePageType == typeof(UsageHistoryPage))
            NavView.SelectedItem = UsageNavItem;
        var isLibraryPage = IsLibraryPage(e.SourcePageType);
        LibrarySelectorBar.Visibility = isLibraryPage
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!isLibraryPage)
            return;

        var selectedItem = e.SourcePageType switch
        {
            var type when type == typeof(CharacterGalleryPage) => CharactersSelectorItem,
            var type when type == typeof(CoordinateGalleryPage) => CoordinatesSelectorItem,
            _ => ScenesSelectorItem,
        };

        if (LibrarySelectorBar.SelectedItem != selectedItem)
        {
            _suppressLibrarySelectionChanged = true;
            LibrarySelectorBar.SelectedItem = selectedItem;
            _suppressLibrarySelectionChanged = false;
        }

        UpdateLibrarySelectorEmphasis();
        NavView.SelectedItem = LibraryNavItem;
    }

    /// <summary>
    /// Type sizes for the library selector: the selected entry is the page's
    /// title, the rest are small and quiet.
    /// </summary>
    /// <remarks>
    /// The browser below used to print the same word as a heading two rows
    /// down. Rather than drop one of the two, the selector became the heading —
    /// so the size difference is not decoration, it is what tells the reader
    /// which of these three words is the title of what they are looking at.
    /// </remarks>
    private const double SelectedLibraryFontSize = 28;
    private const double UnselectedLibraryFontSize = 16;

    private void UpdateLibrarySelectorEmphasis()
    {
        foreach (var item in LibrarySelectorBar.Items)
        {
            var selected = ReferenceEquals(item, LibrarySelectorBar.SelectedItem);
            // Keep the selected page legible as a heading without animating
            // FontSize, which would force a layout pass on every frame.
            item.FontWeight = selected
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
            // Bottom, not centre: mixed type sizes on one line read as a
            // heading only when they share a baseline. Centred, the small
            // words float half a line above the big one.
            item.VerticalAlignment = VerticalAlignment.Bottom;
            SetSelectorEmphasis(item, selected ? SelectedLibraryFontSize : UnselectedLibraryFontSize, selected ? 1 : 0.6);
        }
    }

    /// <summary>The independent opacity transition currently running on each entry.</summary>
    private readonly Dictionary<Control, Storyboard> _emphasisTransitions = [];

    private void SetSelectorEmphasis(Control item, double fontSize, double opacity)
    {
        if (_emphasisTransitions.Remove(item, out var running)) running.Stop();
        item.FontSize = fontSize;

        if (!CardMotion.AnimationsEnabled || Math.Abs(item.Opacity - opacity) < 0.01)
        {
            item.Opacity = opacity;
            return;
        }

        var storyboard = new Storyboard();
        storyboard.Children.Add(Track(item, "Opacity", opacity));
        storyboard.Completed += (_, _) =>
        {
            if (!_emphasisTransitions.TryGetValue(item, out var current) || !ReferenceEquals(current, storyboard)) return;
            _emphasisTransitions.Remove(item);
            item.Opacity = opacity;
        };
        _emphasisTransitions[item] = storyboard;
        storyboard.Begin();
    }

    private static Timeline Track(DependencyObject target, string property, double to)
    {
        var frames = new DoubleAnimationUsingKeyFrames();
        frames.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(CardMotion.FlyDuration),
            Value = to,
            KeySpline = new KeySpline
            {
                ControlPoint1 = new Windows.Foundation.Point(0.4, 0),
                ControlPoint2 = new Windows.Foundation.Point(0.2, 1),
            },
        });
        Storyboard.SetTarget(frames, target);
        Storyboard.SetTargetProperty(frames, property);
        return frames;
    }

    private static bool IsLibraryPage(Type? pageType) =>
        pageType == typeof(GalleryPage)
        || pageType == typeof(CharacterGalleryPage)
        || pageType == typeof(CoordinateGalleryPage);

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
            NavFrame.Navigate(typeof(SettingsPage));
        else if (args.SelectedItem is NavigationViewItem item)
            NavigateToNavItem(item);
    }

    /// <summary>
    /// Handles clicking the entry that is already selected.
    /// </summary>
    /// <remarks>
    /// <see cref="NavigationView.SelectionChanged"/> does not fire when the
    /// invoked entry is the selected one, and opening a card's details leaves
    /// the pane still highlighting the section it came from. Clicking that
    /// highlighted entry to get back therefore did nothing at all.
    ///
    /// Deliberately limited to that case: an ordinary click on another entry
    /// raises both events, and acting on both would navigate twice and leave a
    /// duplicate on the back stack.
    /// </remarks>
    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            if (NavFrame.CurrentSourcePageType != typeof(SettingsPage))
                NavFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.InvokedItemContainer is NavigationViewItem item
            && ReferenceEquals(sender.SelectedItem, item))
        {
            NavigateToNavItem(item);
        }
    }

    private void NavigateToNavItem(NavigationViewItem item)
    {
        switch (item.Tag)
        {
            case "discovery":
                if (NavFrame.CurrentSourcePageType != typeof(RandomDiscoveryPage))
                    NavFrame.Navigate(typeof(RandomDiscoveryPage));
                break;
            case "library":
                NavigateToSelectedLibraryPage();
                break;
            case "authors" when App.Services.GetRequiredService<AuthorsViewModel>().HasProviderTabs:
                if (NavFrame.CurrentSourcePageType != typeof(AuthorsPage))
                    NavFrame.Navigate(typeof(AuthorsPage));
                break;
            case "localsources":
                if (NavFrame.CurrentSourcePageType != typeof(LocalSourcesPage))
                    NavFrame.Navigate(typeof(LocalSourcesPage));
                break;
            case "usage":
                if (NavFrame.CurrentSourcePageType != typeof(UsageHistoryPage))
                    NavFrame.Navigate(typeof(UsageHistoryPage));
                break;
            case "tags":
                if (NavFrame.CurrentSourcePageType != typeof(TagCloudPage))
                    NavFrame.Navigate(typeof(TagCloudPage));
                break;
            case "import" when App.Services.GetService<ImportViewModel>() is not null:
                if (NavFrame.CurrentSourcePageType != typeof(ImportPage))
                    NavFrame.Navigate(typeof(ImportPage));
                break;
        }
    }
}

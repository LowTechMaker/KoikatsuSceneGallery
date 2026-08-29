using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;

namespace KoikatsuSceneGallery.Pages;

internal enum FriendDetailTarget
{
    SelfProfile,
}

public sealed partial class FriendsPage : Page
{
    private static readonly ResourceLoader ResLoader = new();

    private readonly FriendService _friendService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly SelfProfileFolderAccessCoordinator
        _selfProfileFolderAccess;
    private readonly IAppLogger _logger;

    public FriendsViewModel ViewModel { get; }

    public static BitmapImage? CreateAvatarSource(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? null
            : new BitmapImage(new Uri(path)) { DecodePixelWidth = 96 };

    public FriendsPage()
    {
        ViewModel = App.Services.GetRequiredService<FriendsViewModel>();
        _friendService = App.Services.GetRequiredService<FriendService>();
        _settingsViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        _selfProfileFolderAccess = new(
            _friendService,
            _settingsViewModel);
        _logger = App.Services.GetRequiredService<IAppLogger>();

        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        FileWarningInfoBar.IsOpen = !_settingsViewModel.FriendFileWarningShown;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Activate();
        FileWarningInfoBar.IsOpen = !_settingsViewModel.FriendFileWarningShown;
    }

    private void SearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args) =>
        ViewModel.SearchText = sender.Text;

    private void FriendsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FriendListItemViewModel friend)
            Frame.Navigate(typeof(FriendDetailPage), friend.Id);
    }

    private void SelfProfile_Click(object sender, RoutedEventArgs e) =>
        RunFriendAction("Friends.OpenSelfProfile", OpenSelfProfileAsync);

    private async Task OpenSelfProfileAsync()
    {
        if (!await _selfProfileFolderAccess.EnsureAnyFolderAsync(XamlRoot))
            return;

        Frame.Navigate(
            typeof(FriendDetailPage),
            FriendDetailTarget.SelfProfile);
    }

    private void FriendsList_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue
            || args.Item is not FriendListItemViewModel friend
            || args.ItemContainer is not ListViewItem container)
        {
            return;
        }

        AutomationProperties.SetName(container, friend.Name);
        AutomationProperties.SetAutomationId(
            container,
            $"FriendItem_{friend.Id:N}");
    }

    private void CardDropTarget_DragEnter(object sender, DragEventArgs e)
    {
        if (!CardDragDropHelper.ContainsStorageItems(e.DataView))
            return;

        if (sender is UIElement target)
            target.Opacity = 0.72;
        ConfigureCardDrop(e, ReferenceEquals(sender, SelfProfileButton));
    }

    private void CardDropTarget_DragOver(object sender, DragEventArgs e)
    {
        if (CardDragDropHelper.ContainsStorageItems(e.DataView))
            ConfigureCardDrop(
                e,
                ReferenceEquals(sender, SelfProfileButton));
    }

    private void CardDropTarget_DragLeave(
        object sender,
        DragEventArgs e) => RestoreCardDropTarget(sender);

    private void CardDropTarget_Drop(object sender, DragEventArgs e)
    {
        RestoreCardDropTarget(sender);
        var isSelfProfile = ReferenceEquals(sender, SelfProfileButton);
        var friend = (sender as FrameworkElement)?.Tag
            as FriendListItemViewModel;
        if (!isSelfProfile && friend is null)
            return;

        var deferral = e.GetDeferral();
        RunFriendAction("Friends.DropCards", async () =>
        {
            try
            {
                var cardPaths = await CardDragDropHelper
                    .GetValidCardPathsAsync(e.DataView, _friendService);
                if (cardPaths.Count == 0)
                {
                    ShowOperationStatus(
                        ResLoader.GetString("FriendDetail_NoValidCards"),
                        InfoBarSeverity.Warning);
                    return;
                }

                if (isSelfProfile
                    && !await _selfProfileFolderAccess
                        .EnsureAnyFolderAsync(XamlRoot))
                {
                    return;
                }

                var added = isSelfProfile
                    ? await _friendService.AssignSelfProfileCardsAsync(cardPaths)
                    : await _friendService.AssignCardsAsync(
                        friend!.Id,
                        cardPaths);
                if (added == 0)
                {
                    ShowOperationStatus(
                        ResLoader.GetString("FriendDetail_NoNewCardLinks"),
                        InfoBarSeverity.Informational);
                    return;
                }

                ShowOperationStatus(
                    string.Format(
                        ResLoader.GetString("FriendDetail_CardsLinked"),
                        added),
                    InfoBarSeverity.Success);
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private static void ConfigureCardDrop(DragEventArgs e, bool isSelfProfile)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = ResLoader.GetString(
            isSelfProfile
                ? "SelfProfile_DropCaption"
                : "FriendDetail_DropCaption");
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
    }

    private static void RestoreCardDropTarget(object sender)
    {
        if (sender is UIElement target)
            target.Opacity = 1;
    }

    private void AddFriend_Click(object sender, RoutedEventArgs e) =>
        RunFriendAction("Friends.Add", AddFriendAsync);

    private async Task AddFriendAsync()
    {
        var sceneRoots = GetConfiguredRoots(_settingsViewModel.FolderPaths);
        var characterRoots = GetConfiguredRoots(
            _settingsViewModel.CharacterFolderPaths);
        var coordinateRoots = GetConfiguredRoots(
            _settingsViewModel.CoordinateFolderPaths);
        var hasConfiguredRoot = sceneRoots.Count > 0
            || characterRoots.Count > 0
            || coordinateRoots.Count > 0;

        var nameBox = new TextBox
        {
            Header = ResLoader.GetString("Friends_NameField"),
            PlaceholderText = ResLoader.GetString("Friends_NamePlaceholder"),
        };
        var createFolderCheckBox = new CheckBox
        {
            Content = ResLoader.GetString("Friends_CreateFolderOption"),
            IsChecked = false,
            IsEnabled = hasConfiguredRoot,
        };
        AutomationProperties.SetAutomationId(nameBox, "FriendNameTextBox");
        AutomationProperties.SetAutomationId(
            createFolderCheckBox,
            "FriendCreateFolderCheckBox");

        var folderOptionsPanel = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetAutomationId(
            folderOptionsPanel,
            "FriendFolderOptionsPanel");
        folderOptionsPanel.Children.Add(new TextBlock
        {
            Text = ResLoader.GetString("Friends_PersonalFolderHint"),
            TextWrapping = TextWrapping.Wrap,
        });
        var sceneSelector = AddConfiguredRootChoice(
            folderOptionsPanel,
            ResLoader.GetString("Friends_ScenePathHeader"),
            sceneRoots,
            "FriendSceneRootComboBox");
        var characterSelector = AddConfiguredRootChoice(
            folderOptionsPanel,
            ResLoader.GetString("Friends_CharacterPathHeader"),
            characterRoots,
            "FriendCharacterRootComboBox");
        var coordinateSelector = AddConfiguredRootChoice(
            folderOptionsPanel,
            ResLoader.GetString("Friends_CoordinatePathHeader"),
            coordinateRoots,
            "FriendCoordinateRootComboBox");

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(nameBox);
        content.Children.Add(createFolderCheckBox);
        if (hasConfiguredRoot)
        {
            content.Children.Add(folderOptionsPanel);
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = ResLoader.GetString("Friends_NoConfiguredPaths"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString("Friends_AddDialogTitle"),
            Content = content,
            PrimaryButtonText = ResLoader.GetString("Common_Add"),
            CloseButtonText = ResLoader.GetString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        nameBox.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(nameBox.Text);
        createFolderCheckBox.Checked += (_, _) =>
            folderOptionsPanel.Visibility = Visibility.Visible;
        createFolderCheckBox.Unchecked += (_, _) =>
            folderOptionsPanel.Visibility = Visibility.Collapsed;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        FriendFolderRoots? roots = null;
        if (createFolderCheckBox.IsChecked == true)
        {
            roots = new FriendFolderRoots(
                GetSelectedRoot(sceneRoots, sceneSelector),
                GetSelectedRoot(characterRoots, characterSelector),
                GetSelectedRoot(coordinateRoots, coordinateSelector));
            if (!await ConfirmExistingFriendFoldersAsync(
                    nameBox.Text,
                    roots))
            {
                return;
            }
        }

        await _friendService.AddAsync(nameBox.Text, roots);
    }

    private async Task<bool> ConfirmExistingFriendFoldersAsync(
        string friendName,
        FriendFolderRoots roots)
    {
        var existingFolders = new List<(
            string Header,
            string Path,
            CardOwnerRecord? Owner)>();
        AddExistingFolder(
            ResLoader.GetString("Friends_ScenePathHeader"),
            roots.SceneRootPath);
        AddExistingFolder(
            ResLoader.GetString("Friends_CharacterPathHeader"),
            roots.CharacterRootPath);
        AddExistingFolder(
            ResLoader.GetString("Friends_CoordinatePathHeader"),
            roots.CoordinateRootPath);

        var conflicts = existingFolders
            .Where(folder => folder.Owner is not null)
            .ToList();
        if (conflicts.Count > 0)
        {
            var message = string.Join(
                Environment.NewLine + Environment.NewLine,
                conflicts.Select(folder => string.Format(
                    ResLoader.GetString("Friends_FolderOwnedEntry"),
                    folder.Header,
                    folder.Path,
                    folder.Owner!.Name)));
            var conflictDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = ResLoader.GetString(
                    "Friends_FolderOwnedDialogTitle"),
                Content = new TextBlock
                {
                    Text = string.Format(
                        ResLoader.GetString(
                            "Friends_FolderOwnedDialogMessage"),
                        message),
                    TextWrapping = TextWrapping.Wrap,
                },
                CloseButtonText = ResLoader.GetString("Common_OK"),
                DefaultButton = ContentDialogButton.Close,
            };
            AutomationProperties.SetAutomationId(
                conflictDialog,
                "FriendFolderOwnedDialog");
            await conflictDialog.ShowAsync();
            return false;
        }

        if (existingFolders.Count == 0)
            return true;

        var paths = string.Join(
            Environment.NewLine,
            existingFolders.Select(folder => string.Format(
                ResLoader.GetString("Friends_ConfiguredPathSummary"),
                folder.Header,
                folder.Path)));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString(
                "Friends_ExistingFolderDialogTitle"),
            Content = new TextBlock
            {
                Text = string.Format(
                    ResLoader.GetString(
                        "Friends_ExistingFolderDialogMessage"),
                    paths),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = ResLoader.GetString(
                "Friends_BindExistingFolderButton"),
            CloseButtonText = ResLoader.GetString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(
            dialog,
            "FriendExistingFolderDialog");
        return await dialog.ShowAsync() == ContentDialogResult.Primary;

        void AddExistingFolder(
            string header,
            string? configuredRoot)
        {
            if (string.IsNullOrWhiteSpace(configuredRoot))
                return;

            var path = FriendFolderLayout.FindExistingPersonalFolderPath(
                configuredRoot,
                friendName);
            if (path is null)
                return;

            existingFolders.Add((
                header,
                path,
                _friendService.FindFolderOwner(path)));
        }
    }

    private static IReadOnlyList<string> GetConfiguredRoots(
        IEnumerable<string> roots) =>
        roots.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? GetSelectedRoot(
        IReadOnlyList<string> roots,
        ComboBox? selector) =>
        roots.Count switch
        {
            0 => null,
            1 => roots[0],
            _ => selector?.SelectedItem as string,
        };

    private static ComboBox? AddConfiguredRootChoice(
        StackPanel panel,
        string header,
        IReadOnlyList<string> roots,
        string automationId)
    {
        if (roots.Count == 0)
            return null;

        if (roots.Count == 1)
        {
            var summary = new TextBlock
            {
                Text = string.Format(
                    ResLoader.GetString("Friends_ConfiguredPathSummary"),
                    header,
                    roots[0]),
                TextWrapping = TextWrapping.Wrap,
            };
            AutomationProperties.SetAutomationId(
                summary,
                automationId.Replace("ComboBox", "SummaryText"));
            panel.Children.Add(summary);
            return null;
        }

        var selector = new ComboBox
        {
            Header = header,
            ItemsSource = roots,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(selector, automationId);
        panel.Children.Add(selector);
        return selector;
    }

    private void RenameFriend_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: FriendListItemViewModel friend })
            return;

        RunFriendAction(
            "Friends.Rename",
            () => RenameFriendAsync(friend));
    }

    private async Task RenameFriendAsync(FriendListItemViewModel friend)
    {
        var nameBox = new TextBox
        {
            Header = ResLoader.GetString("Friends_NameField"),
            Text = friend.Name,
            SelectionStart = friend.Name.Length,
        };
        AutomationProperties.SetAutomationId(nameBox, "FriendRenameTextBox");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString("Friends_RenameDialogTitle"),
            Content = nameBox,
            PrimaryButtonText = ResLoader.GetString("Common_Save"),
            CloseButtonText = ResLoader.GetString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary
            && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            await _friendService.RenameAsync(friend.Id, nameBox.Text);
        }
    }

    private void DeleteFriend_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: FriendListItemViewModel friend })
            return;

        RunFriendAction(
            "Friends.Delete",
            () => DeleteFriendAsync(friend));
    }

    private async Task DeleteFriendAsync(FriendListItemViewModel friend)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString("Friends_DeleteDialogTitle"),
            Content = string.Format(
                ResLoader.GetString("Friends_DeleteDialogMessage"),
                friend.Name),
            PrimaryButtonText = ResLoader.GetString("Common_Delete"),
            CloseButtonText = ResLoader.GetString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await _friendService.DeleteAsync(friend.Id);
    }

    private void FileWarningInfoBar_Closed(
        InfoBar sender,
        InfoBarClosedEventArgs args)
    {
        if (args.Reason != InfoBarCloseReason.CloseButton
            || _settingsViewModel.FriendFileWarningShown)
        {
            return;
        }

        UiEventGuard.Run(
            _logger,
            "Friends.MarkFileWarningShown",
            _settingsViewModel.MarkFriendFileWarningShownAsync,
            _ =>
            {
                FileWarningInfoBar.IsOpen = true;
                ShowOperationFailure();
            });
    }

    private void RunFriendAction(string operation, Func<Task> action) =>
        UiEventGuard.Run(
            _logger,
            operation,
            action,
            _ => ShowOperationFailure());

    private void ShowOperationFailure()
    {
        ShowOperationStatus(
            ResLoader.GetString("Friends_OperationFailed"),
            InfoBarSeverity.Error);
    }

    private void ShowOperationStatus(
        string message,
        InfoBarSeverity severity)
    {
        OperationStatusInfoBar.Message = message;
        OperationStatusInfoBar.Severity = severity;
        OperationStatusInfoBar.IsOpen = true;
    }
}

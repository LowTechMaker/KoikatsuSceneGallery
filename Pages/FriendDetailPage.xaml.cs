using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.System;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class FriendDetailPage : Page
{
    private static readonly ResourceLoader ResLoader = new();

    private readonly FriendService _friendService;
    private readonly GalleryViewModel _sceneViewModel;
    private readonly CharacterGalleryViewModel _characterViewModel;
    private readonly CoordinateGalleryViewModel _coordinateViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly SelfProfileFolderAccessCoordinator
        _selfProfileFolderAccess;
    private readonly IAppLogger _logger;
    private readonly ThumbnailRequestController _thumbnailRequests;

    private Guid _ownerId;
    private CardOwnerRecord? _owner;
    private bool _isSelfProfile;
    private CardType _category = CardType.Scene;
    private bool _eventsSubscribed;
    private bool _cardRefreshQueued;
    private bool _suppressCategorySelection;
    private bool _categorySelectionPending;
    private double _appliedCardAspect = -1;
    private double _appliedCardPanelWidth = -1;

    public ObservableCollection<FriendCardItemViewModel> Cards { get; } = [];

    public FriendDetailPage()
    {
        _friendService = App.Services.GetRequiredService<FriendService>();
        _sceneViewModel = App.Services.GetRequiredService<GalleryViewModel>();
        _characterViewModel = App.Services.GetRequiredService<CharacterGalleryViewModel>();
        _coordinateViewModel = App.Services.GetRequiredService<CoordinateGalleryViewModel>();
        _settingsViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        _selfProfileFolderAccess = new(
            _friendService,
            _settingsViewModel);
        _logger = App.Services.GetRequiredService<IAppLogger>();
        _thumbnailRequests = new ThumbnailRequestController(
            App.Services.GetRequiredService<ThumbnailService>(),
            _logger,
            "FriendDetail.GenerateThumbnail");

        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is FriendDetailTarget.SelfProfile)
        {
            _isSelfProfile = true;
            _owner = _friendService.SelfProfile;
            _ownerId = _owner.Id;
        }
        else if (e.Parameter is Guid friendId)
        {
            _isSelfProfile = false;
            _ownerId = friendId;
            _owner = _friendService.Find(friendId);
        }
        else
        {
            App.TryGoBack(Frame);
            return;
        }

        if (_owner is null)
        {
            App.TryGoBack(Frame);
            return;
        }

        if (_isSelfProfile)
        {
            var availableCategory =
                _selfProfileFolderAccess.GetFirstAvailableCategory();
            if (availableCategory is null)
            {
                App.TryGoBack(Frame);
                return;
            }

            _category = availableCategory.Value;
            RestoreCategorySelection();
        }

        SubscribeEvents();
        _thumbnailRequests.Activate();
        UpdateHeader();
        RefreshCards();
        EnsureSelectedCategoryLoaded();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _thumbnailRequests.Cancel();
        UnsubscribeEvents();
        base.OnNavigatedFrom(e);
    }

    private void SubscribeEvents()
    {
        if (_eventsSubscribed)
            return;

        _friendService.FriendChanged += OnFriendChanged;
        _friendService.SelfProfileChanged += OnSelfProfileChanged;
        _sceneViewModel.CardsReloaded += OnSceneCardsReloaded;
        _characterViewModel.CardsReloaded += OnCharacterCardsReloaded;
        _coordinateViewModel.CardsReloaded += OnCoordinateCardsReloaded;
        _sceneViewModel.CardsChanged += OnSceneCardsChanged;
        _characterViewModel.CardsChanged += OnCharacterCardsChanged;
        _coordinateViewModel.CardsChanged += OnCoordinateCardsChanged;
        _eventsSubscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!_eventsSubscribed)
            return;

        _friendService.FriendChanged -= OnFriendChanged;
        _friendService.SelfProfileChanged -= OnSelfProfileChanged;
        _sceneViewModel.CardsReloaded -= OnSceneCardsReloaded;
        _characterViewModel.CardsReloaded -= OnCharacterCardsReloaded;
        _coordinateViewModel.CardsReloaded -= OnCoordinateCardsReloaded;
        _sceneViewModel.CardsChanged -= OnSceneCardsChanged;
        _characterViewModel.CardsChanged -= OnCharacterCardsChanged;
        _coordinateViewModel.CardsChanged -= OnCoordinateCardsChanged;
        _eventsSubscribed = false;
    }

    private void OnFriendChanged(FriendChange change)
    {
        if (_isSelfProfile || change.FriendId != _ownerId)
            return;

        if (DispatcherQueue.HasThreadAccess)
            ApplyOwnerChange(change.Kinds);
        else
            DispatcherQueue.TryEnqueue(() => ApplyOwnerChange(change.Kinds));
    }

    private void OnSelfProfileChanged(FriendChangeKinds kinds)
    {
        if (!_isSelfProfile)
            return;

        if (DispatcherQueue.HasThreadAccess)
            ApplyOwnerChange(kinds);
        else
            DispatcherQueue.TryEnqueue(() => ApplyOwnerChange(kinds));
    }

    private void ApplyOwnerChange(FriendChangeKinds kinds)
    {
        if (!_eventsSubscribed)
            return;

        _owner = _isSelfProfile
            ? _friendService.SelfProfile
            : _friendService.Find(_ownerId);
        if (_owner is null)
            return;

        if (kinds.HasFlag(FriendChangeKinds.Identity)
            || kinds.HasFlag(FriendChangeKinds.Avatar)
            || kinds.HasFlag(FriendChangeKinds.Folders))
        {
            UpdateHeader();
        }

        if (kinds.HasFlag(FriendChangeKinds.Folders)
            || kinds.HasFlag(FriendChangeKinds.Cards))
        {
            RefreshCards();
        }
    }

    private void OnSceneCardsReloaded() =>
        OnCardsReloaded(CardType.Scene);

    private void OnCharacterCardsReloaded() =>
        OnCardsReloaded(CardType.Character);

    private void OnCoordinateCardsReloaded() =>
        OnCardsReloaded(CardType.Coordinate);

    private void OnSceneCardsChanged() =>
        OnCardsChanged(CardType.Scene);

    private void OnCharacterCardsChanged() =>
        OnCardsChanged(CardType.Character);

    private void OnCoordinateCardsChanged() =>
        OnCardsChanged(CardType.Coordinate);

    private void OnCardsReloaded(CardType cardType)
    {
        if (DispatcherQueue.HasThreadAccess)
            ApplyCardsReloaded(cardType);
        else
            DispatcherQueue.TryEnqueue(() => ApplyCardsReloaded(cardType));
    }

    private void ApplyCardsReloaded(CardType cardType)
    {
        if (_eventsSubscribed && _category == cardType)
            RefreshCards();
    }

    private void OnCardsChanged(CardType cardType)
    {
        if (!_eventsSubscribed
            || _category != cardType
            || _cardRefreshQueued)
        {
            return;
        }

        _cardRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _cardRefreshQueued = false;
                if (_eventsSubscribed && _category == cardType)
                    RefreshCards();
            }))
        {
            _cardRefreshQueued = false;
        }
    }

    private void UpdateHeader()
    {
        if (_owner is null)
            return;

        FriendNameText.Text = _owner.Name;
        FriendPicture.DisplayName = _owner.Name;
        var avatarPath = _friendService.GetAvatarPath(_owner);
        FriendPicture.ProfilePicture = CreateAvatarSource(avatarPath);
        RemoveAvatarMenuItem.IsEnabled =
            _owner.AvatarPath is not null
            || _owner.AvatarAuthorProviderId is not null
            || _owner.AvatarAuthorId is not null;
        SelfProfileBadge.Visibility = _isSelfProfile
            ? Visibility.Visible
            : Visibility.Collapsed;
        DeleteFriendMenuSeparator.Visibility = _isSelfProfile
            ? Visibility.Collapsed
            : Visibility.Visible;
        DeleteFriendMenuItem.Visibility = _isSelfProfile
            ? Visibility.Collapsed
            : Visibility.Visible;
        AutomationProperties.SetName(
            FriendAvatarButton,
            ResLoader.GetString(
                _isSelfProfile
                    ? "SelfProfile_AvatarAutomationName"
                    : "FriendDetail_AvatarAutomationName"));
        AutomationProperties.SetName(
            FriendDetailMoreButton,
            ResLoader.GetString(
                _isSelfProfile
                    ? "SelfProfile_MoreAutomationName"
                    : "FriendDetail_MoreAutomationName"));
        var folderPath = _friendService.GetFolder(_owner, _category);
        var hasFolderPath = !string.IsNullOrWhiteSpace(folderPath);
        var folderExists = hasFolderPath && Directory.Exists(folderPath);
        FolderPathText.Text = hasFolderPath
            ? folderPath
            : ResLoader.GetString("FriendDetail_NoFolder");
        OpenFolderButton.Visibility = folderExists
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetFolderButton.Visibility = folderExists
            ? Visibility.Collapsed
            : Visibility.Visible;
        CreateVariantFoldersButton.Visibility = folderExists
            && _category == CardType.Character
            && !FriendFolderLayout.HasCharacterVariantFolders(folderPath!)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ClearFolderMenuItem.IsEnabled = hasFolderPath;
    }

    private static BitmapImage? CreateAvatarSource(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? null
            : new BitmapImage(new Uri(path)) { DecodePixelWidth = 128 };

    private void SetAvatarMenuItem_Click(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
            FriendAvatarButton.Flyout?.ShowAt(FriendAvatarButton));

    private void ChooseLocalAvatar_Click(object sender, RoutedEventArgs e) =>
        RunFriendAction(
            "FriendDetail.ChooseLocalAvatar",
            ChooseLocalAvatarAsync);

    private async Task ChooseLocalAvatarAsync()
    {
        if (_owner is null)
            return;

        var picker = new FileOpenPicker(
            XamlRoot.ContentIslandEnvironment.AppWindowId);
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".webp");
        picker.FileTypeFilter.Add(".bmp");
        var file = await picker.PickSingleFileAsync();
        if (file is null || string.IsNullOrWhiteSpace(file.Path))
            return;
        if (!await IsDecodableImageAsync(file.Path))
        {
            ShowStatus(
                ResLoader.GetString("FriendDetail_InvalidAvatarImage"),
                InfoBarSeverity.Warning);
            return;
        }

        try
        {
            await SetLocalAvatarForOwnerAsync(file.Path);
        }
        catch (ArgumentException)
        {
            ShowStatus(
                ResLoader.GetString("FriendDetail_InvalidAvatarImage"),
                InfoBarSeverity.Warning);
            return;
        }
        catch (FileNotFoundException)
        {
            ShowStatus(
                ResLoader.GetString("FriendDetail_InvalidAvatarImage"),
                InfoBarSeverity.Warning);
            return;
        }

        ShowStatus(
            GetOwnerString(
                "FriendDetail_AvatarUpdated",
                "SelfProfile_AvatarUpdated"),
            InfoBarSeverity.Success);
    }

    private static async Task<bool> IsDecodableImageAsync(string filePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return decoder.PixelWidth > 0 && decoder.PixelHeight > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ChooseAuthorAvatar_Click(object sender, RoutedEventArgs e) =>
        RunFriendAction(
            "FriendDetail.ChooseAuthorAvatar",
            ChooseAuthorAvatarAsync);

    private async Task ChooseAuthorAvatarAsync()
    {
        if (_owner is null)
            return;

        var authors = _friendService.GetKnownAuthorAvatars();
        if (authors.Count == 0)
        {
            ShowStatus(
                ResLoader.GetString("FriendDetail_NoKnownAuthorAvatars"),
                InfoBarSeverity.Informational);
            return;
        }

        var authorOptions = authors
            .Select(author => new FriendAuthorAvatarItemViewModel(
                author,
                $"FriendAuthor_{GetStableId(
                    $"{author.Key.ProviderId}\n{author.Key.Id}")}"))
            .ToList();
        var authorList = new ListView
        {
            ItemTemplate = (DataTemplate)Resources["FriendAuthorAvatarTemplate"],
            MinWidth = 440,
            MaxHeight = 420,
            SelectionMode = ListViewSelectionMode.Single,
        };
        AutomationProperties.SetAutomationId(
            authorList,
            "FriendAuthorAvatarList");
        authorList.ItemsSource = authorOptions;
        var searchBox = new AutoSuggestBox
        {
            PlaceholderText = ResLoader.GetString(
                "FriendDetail_AuthorAvatarSearchPlaceholder"),
            QueryIcon = new SymbolIcon(Symbol.Find),
        };
        AutomationProperties.SetAutomationId(
            searchBox,
            "FriendAuthorAvatarSearchBox");
        searchBox.TextChanged += (_, _) =>
        {
            var query = searchBox.Text.Trim();
            authorList.ItemsSource = query.Length == 0
                ? authorOptions
                : authorOptions.Where(author =>
                        author.Name.Contains(
                            query,
                            StringComparison.CurrentCultureIgnoreCase)
                        || author.ProfileUrl.Contains(
                            query,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
        };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(searchBox);
        content.Children.Add(authorList);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString("FriendDetail_SelectAuthorAvatarTitle"),
            Content = content,
            PrimaryButtonText = ResLoader.GetString("Common_Select"),
            CloseButtonText = ResLoader.GetString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        authorList.SelectionChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = authorList.SelectedItem is not null;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || authorList.SelectedItem
                is not FriendAuthorAvatarItemViewModel option)
        {
            return;
        }

        await SetAuthorAvatarForOwnerAsync(option.Author);
        ShowStatus(
            GetOwnerString(
                "FriendDetail_AvatarUpdated",
                "SelfProfile_AvatarUpdated"),
            InfoBarSeverity.Success);
    }

    private void RemoveAvatar_Click(object sender, RoutedEventArgs e) =>
        RunFriendAction(
            "FriendDetail.RemoveAvatar",
            RemoveAvatarAsync);

    private async Task RemoveAvatarAsync()
    {
        if (_owner is null)
            return;

        await ClearAvatarForOwnerAsync();
        ShowStatus(
            GetOwnerString(
                "FriendDetail_AvatarRemoved",
                "SelfProfile_AvatarRemoved"),
            InfoBarSeverity.Success);
    }

    private void EnsureSelectedCategoryLoaded()
    {
        switch (_category)
        {
            case CardType.Character
                when !_characterViewModel.HasCompletedLoad
                     && !_characterViewModel.IsLoading:
                _characterViewModel.LoadCardsCommand.ExecuteAsync(null)
                    .Observe(_logger, "FriendDetail.LoadCharacters");
                break;
            case CardType.Coordinate
                when !_coordinateViewModel.HasCompletedLoad
                     && !_coordinateViewModel.IsLoading:
                _coordinateViewModel.LoadCardsCommand.ExecuteAsync(null)
                    .Observe(_logger, "FriendDetail.LoadCoordinates");
                break;
            case CardType.Scene
                when !_sceneViewModel.HasCompletedLoad
                     && !_sceneViewModel.IsLoading:
                _sceneViewModel.LoadCardsCommand.ExecuteAsync(null)
                    .Observe(_logger, "FriendDetail.LoadScenes");
                break;
        }
    }

    private void RefreshCards()
    {
        if (_owner is null)
            return;

        IEnumerable<CardBase> source = _category switch
        {
            CardType.Character => _characterViewModel.Cards,
            CardType.Coordinate => _coordinateViewModel.Cards,
            _ => _sceneViewModel.Cards,
        };

        var assigned = source
            .Where(card => _friendService.IsAssignedTo(
                _owner,
                card.FilePath,
                _category))
            .OrderByDescending(card => card.DateModified)
            .Select(card => new FriendCardItemViewModel
            {
                Card = card,
                AutomationIdSuffix = GetStableId(card.FilePath),
            })
            .ToList();

        Cards.Clear();
        foreach (var card in assigned)
            Cards.Add(card);

        EmptyCardsPanel.Visibility = Cards.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        _appliedCardAspect = -1;
        DispatcherQueue.TryEnqueue(UpdateFriendCardLayout);
    }

    private void CardTypeSelector_SelectionChanged(
        SelectorBar sender,
        SelectorBarSelectionChangedEventArgs args)
    {
        if (_suppressCategorySelection)
            return;

        var requestedCategory = sender.SelectedItem switch
        {
            var item when item == CharactersSelectorItem => CardType.Character,
            var item when item == CoordinatesSelectorItem => CardType.Coordinate,
            _ => CardType.Scene,
        };

        if (requestedCategory == _category)
            return;

        if (_categorySelectionPending)
        {
            RestoreCategorySelection();
            return;
        }

        if (!_isSelfProfile
            || _selfProfileFolderAccess.HasExistingBoundFolder(
                requestedCategory))
        {
            ApplyCategory(requestedCategory);
            return;
        }

        _categorySelectionPending = true;
        RunFriendAction(
            "FriendDetail.EnsureSelfProfileFolder",
            async () =>
            {
                try
                {
                    if (await _selfProfileFolderAccess
                            .EnsureCategoryFolderAsync(
                                requestedCategory,
                                XamlRoot))
                    {
                        ApplyCategory(requestedCategory);
                    }
                    else
                    {
                        RestoreCategorySelection();
                    }
                }
                catch
                {
                    RestoreCategorySelection();
                    throw;
                }
                finally
                {
                    _categorySelectionPending = false;
                }
            });
    }

    private void ApplyCategory(CardType cardType)
    {
        _category = cardType;
        UpdateHeader();
        RefreshCards();
        if (_owner is not null)
            EnsureSelectedCategoryLoaded();
    }

    private void RestoreCategorySelection()
    {
        _suppressCategorySelection = true;
        CardTypeSelector.SelectedItem = _category switch
        {
            CardType.Character => CharactersSelectorItem,
            CardType.Coordinate => CoordinatesSelectorItem,
            _ => ScenesSelectorItem,
        };
        _suppressCategorySelection = false;
    }

    private void FriendCardsGrid_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue
            && args.Item is FriendCardItemViewModel item
            && args.ItemContainer is GridViewItem container)
        {
            AutomationProperties.SetName(container, item.FileName);
            AutomationProperties.SetAutomationId(
                container,
                $"FriendCard_{item.AutomationIdSuffix}");
            _thumbnailRequests.Request(item.Card);
        }
    }

    private void FriendCardsGrid_Loaded(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(UpdateFriendCardLayout);

    private void FriendCardsGrid_SizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(UpdateFriendCardLayout);

    private void UpdateFriendCardLayout()
    {
        if (FriendCardsGrid.ItemsPanelRoot is not ItemsWrapGrid panel)
            return;

        var available = panel.ActualWidth > 0
            ? panel.ActualWidth
            : FriendCardsGrid.ActualWidth;
        if (available <= 0)
            return;

        var aspect = GetCurrentCardHeightToWidthRatio();
        if (Math.Abs(available - _appliedCardPanelWidth) < 0.5
            && Math.Abs(aspect - _appliedCardAspect) < 0.001)
        {
            return;
        }

        _appliedCardPanelWidth = available;
        _appliedCardAspect = aspect;

        var desiredWidth = Math.Clamp(
            _settingsViewModel.ThumbnailWidth,
            170,
            360);
        var columns = Math.Max(
            1,
            (int)Math.Floor(available / (desiredWidth + 8)));
        var cellWidth = available / columns - 0.5;
        var imageWidth = Math.Max(0, cellWidth - 18);
        const double filenameAndInsets = 48;

        panel.ItemWidth = cellWidth;
        panel.ItemHeight = imageWidth * aspect + filenameAndInsets;
    }

    private double GetCurrentCardHeightToWidthRatio()
    {
        var ratios = Cards
            .Select(item => item.Card)
            .Where(card => card.Width > 0 && card.Height > 0)
            .Select(card => Math.Clamp(
                (double)card.Height / card.Width,
                0.45,
                1.8))
            .OrderBy(ratio => ratio)
            .ToList();
        if (ratios.Count > 0)
            return ratios[ratios.Count / 2];

        return _category == CardType.Scene
            ? 135.0 / 240.0
            : 352.0 / 252.0;
    }

    private static string GetStableId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..16];
    }

    private void FriendCardsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not FriendCardItemViewModel item)
            return;

        switch (item.Card)
        {
            case CharacterCard character:
                Frame.Navigate(typeof(CharacterDetailPage), character);
                break;
            case CoordinateCard coordinate:
                Frame.Navigate(typeof(CoordinateDetailPage), coordinate);
                break;
            case SceneCard scene:
                Frame.Navigate(typeof(DetailPage), scene);
                break;
        }
    }

    private void FriendCardsGrid_DragItemsStarting(
        object sender,
        DragItemsStartingEventArgs e) =>
        CardDragDropHelper.SetDraggedFiles(
            e.Data,
            e.Items
                .OfType<FriendCardItemViewModel>()
                .Select(item => item.FilePath),
            _logger,
            "FriendDetail.PrepareDragFile");

    private void CardDropArea_DragOver(object sender, DragEventArgs e)
    {
        if (!CardDragDropHelper.ContainsStorageItems(e.DataView))
            return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = GetOwnerString(
            "FriendDetail_DropCaption",
            "SelfProfile_DropCaption");
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
    }

    private void CardDropArea_Drop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        RunFriendAction("FriendDetail.DropCards", async () =>
        {
            try
            {
                if (_owner is null
                    || !CardDragDropHelper.ContainsStorageItems(e.DataView))
                {
                    return;
                }

                var cardPaths = await CardDragDropHelper
                    .GetValidCardPathsAsync(e.DataView, _friendService);
                if (cardPaths.Count == 0)
                {
                    ShowStatus(
                        ResLoader.GetString("FriendDetail_NoValidCards"),
                        InfoBarSeverity.Warning);
                    return;
                }

                var added = await AssignCardsToOwnerAsync(cardPaths);
                if (added == 0)
                {
                    ShowStatus(
                        ResLoader.GetString("FriendDetail_NoNewCardLinks"),
                        InfoBarSeverity.Informational);
                    return;
                }

                ShowStatus(
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

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        RunFriendAction("FriendDetail.OpenFolder", OpenFolderAsync);

    private async Task OpenFolderAsync()
    {
        if (_owner is null
            || _friendService.GetFolder(_owner, _category) is not { } folderPath
            || !Directory.Exists(folderPath))
        {
            ShowStatus(
                ResLoader.GetString("FriendDetail_FolderMissing"),
                InfoBarSeverity.Warning);
            UpdateHeader();
            return;
        }

        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
            if (!await Launcher.LaunchFolderAsync(folder))
            {
                ShowStatus(
                    ResLoader.GetString("FriendDetail_FolderOpenFailed"),
                    InfoBarSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("FriendDetail.OpenFolder", ex, folderPath);
            ShowStatus(
                ResLoader.GetString("FriendDetail_FolderOpenFailed"),
                InfoBarSeverity.Warning);
        }
    }

    private void SetFolder_Click(object sender, RoutedEventArgs e) =>
        RunFriendAction("FriendDetail.SetFolder", SetFolderAsync);

    private async Task SetFolderAsync()
    {
        if (_owner is null)
            return;

        var configuredRoot = await SelectConfiguredRootAsync();
        if (configuredRoot is null)
            return;

        if (!await ConfirmFolderBindingAsync(configuredRoot))
            return;

        var folderPath = await SetFolderForOwnerAsync(configuredRoot);
        ShowStatus(
            string.Format(
                ResLoader.GetString("FriendDetail_FolderSet"),
                folderPath),
            InfoBarSeverity.Success);
    }

    private async Task<bool> ConfirmFolderBindingAsync(string configuredRoot)
    {
        if (_owner is null)
            return false;

        var existingFolder = _isSelfProfile
            ? FriendFolderLayout.FindExistingSelfFolderPath(configuredRoot)
            : FriendFolderLayout.FindExistingPersonalFolderPath(
                configuredRoot,
                _owner.Name);
        if (existingFolder is null
            || _friendService.GetFolder(
                    _owner,
                    _category) is { } currentFolder
                && FriendFolderLayout.AreSamePath(
                    currentFolder,
                    existingFolder))
        {
            return true;
        }

        var assignedOwner = _friendService.FindFolderOwner(
            existingFolder,
            _owner.Id);
        if (assignedOwner is not null)
        {
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
                        string.Format(
                            ResLoader.GetString(
                                "Friends_FolderOwnedEntry"),
                            GetConfiguredRootHeader(),
                            existingFolder,
                            assignedOwner.Name)),
                    TextWrapping = TextWrapping.Wrap,
                },
                CloseButtonText = ResLoader.GetString("Common_OK"),
                DefaultButton = ContentDialogButton.Close,
            };
            AutomationProperties.SetAutomationId(
                conflictDialog,
                "FriendDetailFolderOwnedDialog");
            await conflictDialog.ShowAsync();
            return false;
        }

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
                    string.Format(
                        ResLoader.GetString(
                            "Friends_ConfiguredPathSummary"),
                        GetConfiguredRootHeader(),
                        existingFolder)),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = ResLoader.GetString(
                "Friends_BindExistingFolderButton"),
            CloseButtonText = ResLoader.GetString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(
            dialog,
            "FriendDetailExistingFolderDialog");
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<string?> SelectConfiguredRootAsync()
    {
        var roots = GetConfiguredRootsForCategory();
        if (roots.Count == 0)
        {
            ShowStatus(
                ResLoader.GetString("FriendDetail_NoConfiguredPath"),
                InfoBarSeverity.Warning);
            return null;
        }

        if (roots.Count == 1)
            return roots[0];

        var selector = new ComboBox
        {
            Header = GetConfiguredRootHeader(),
            ItemsSource = roots,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(
            selector,
            "FriendDetailRootComboBox");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString("FriendDetail_SelectFolderPathTitle"),
            Content = selector,
            PrimaryButtonText = ResLoader.GetString("Common_Create"),
            CloseButtonText = ResLoader.GetString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? selector.SelectedItem as string
            : null;
    }

    private IReadOnlyList<string> GetConfiguredRootsForCategory()
    {
        IEnumerable<string> roots = _category switch
        {
            CardType.Character => _settingsViewModel.CharacterFolderPaths,
            CardType.Coordinate => _settingsViewModel.CoordinateFolderPaths,
            _ => _settingsViewModel.FolderPaths,
        };
        return roots.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string GetConfiguredRootHeader() =>
        ResLoader.GetString(_category switch
        {
            CardType.Character => "Friends_CharacterPathHeader",
            CardType.Coordinate => "Friends_CoordinatePathHeader",
            _ => "Friends_ScenePathHeader",
        });

    private void CreateVariantFolders_Click(object sender, RoutedEventArgs e) =>
        RunFriendAction(
            "FriendDetail.CreateVariantFolders",
            CreateVariantFoldersAsync);

    private Task CreateVariantFoldersAsync()
    {
        if (_owner?.CharacterFolderPath is null)
            return Task.CompletedTask;

        var path = EnsureCharacterVariantFoldersForOwner();

        ShowStatus(
            string.Format(
                GetOwnerString(
                    "FriendDetail_VariantFoldersCreated",
                    "SelfProfile_VariantFoldersCreated"),
                path),
            InfoBarSeverity.Success);
        return Task.CompletedTask;
    }

    private void RemoveCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: FriendCardItemViewModel item })
            return;

        RunFriendAction(
            "FriendDetail.RemoveCard",
            () => RemoveCardAsync(item));
    }

    private async Task RemoveCardAsync(FriendCardItemViewModel item)
    {
        if (_owner is null)
            return;

        if (_friendService.IsInsideDedicatedFolder(
                _owner,
                item.FilePath,
                _category))
        {
            ShowStatus(
                GetOwnerString(
                    "FriendDetail_FolderCardCannotUnlink",
                    "SelfProfile_FolderCardCannotUnlink"),
                InfoBarSeverity.Informational);
            return;
        }

        var explicitlyLinked = _owner.CardPaths.Any(path =>
            path.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
        if (!explicitlyLinked)
            return;

        await RemoveCardFromOwnerAsync(item.FilePath);
        ShowStatus(
            ResLoader.GetString("FriendDetail_CardUnlinked"),
            InfoBarSeverity.Success);
    }

    private void RenameFriend_Click(object sender, RoutedEventArgs e) =>
        RunFriendAction("FriendDetail.Rename", RenameFriendAsync);

    private async Task RenameFriendAsync()
    {
        if (_owner is null)
            return;

        var nameBox = new TextBox
        {
            Header = ResLoader.GetString("Friends_NameField"),
            Text = _owner.Name,
            SelectionStart = _owner.Name.Length,
        };
        AutomationProperties.SetAutomationId(
            nameBox,
            "FriendDetailRenameTextBox");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString(
                _isSelfProfile
                    ? "SelfProfile_RenameDialogTitle"
                    : "Friends_RenameDialogTitle"),
            Content = nameBox,
            PrimaryButtonText = ResLoader.GetString("Common_Save"),
            CloseButtonText = ResLoader.GetString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary
            && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            await RenameOwnerAsync(nameBox.Text);
        }
    }

    private void ClearFolder_Click(object sender, RoutedEventArgs e) =>
        RunFriendAction("FriendDetail.ClearFolder", ClearFolderAsync);

    private async Task ClearFolderAsync()
    {
        if (_owner is null
            || _friendService.GetFolder(_owner, _category) is null)
            return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString("FriendDetail_ClearFolderDialogTitle"),
            Content = GetOwnerString(
                "FriendDetail_ClearFolderDialogMessage",
                "SelfProfile_ClearFolderDialogMessage"),
            PrimaryButtonText = ResLoader.GetString("FriendDetail_ClearFolderConfirm"),
            CloseButtonText = ResLoader.GetString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await ClearFolderForOwnerAsync();
    }

    private void DeleteFriend_Click(object sender, RoutedEventArgs e) =>
        RunFriendAction("FriendDetail.Delete", DeleteFriendAsync);

    private async Task DeleteFriendAsync()
    {
        if (_owner is null || _isSelfProfile)
            return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString("Friends_DeleteDialogTitle"),
            Content = string.Format(
                ResLoader.GetString("Friends_DeleteDialogMessage"),
                _owner.Name),
            PrimaryButtonText = ResLoader.GetString("Common_Delete"),
            CloseButtonText = ResLoader.GetString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await _friendService.DeleteAsync(_owner.Id);
        App.TryGoBack(Frame);
    }

    private Task SetLocalAvatarForOwnerAsync(string filePath) =>
        _isSelfProfile
            ? _friendService.SetSelfProfileLocalAvatarAsync(filePath)
            : _friendService.SetLocalAvatarAsync(_ownerId, filePath);

    private Task SetAuthorAvatarForOwnerAsync(AuthorDisplay author) =>
        _isSelfProfile
            ? _friendService.SetSelfProfileAuthorAvatarAsync(author)
            : _friendService.SetAuthorAvatarAsync(_ownerId, author);

    private Task ClearAvatarForOwnerAsync() =>
        _isSelfProfile
            ? _friendService.ClearSelfProfileAvatarAsync()
            : _friendService.ClearAvatarAsync(_ownerId);

    private Task<int> AssignCardsToOwnerAsync(IEnumerable<string> filePaths) =>
        _isSelfProfile
            ? _friendService.AssignSelfProfileCardsAsync(filePaths)
            : _friendService.AssignCardsAsync(_ownerId, filePaths);

    private Task<string> SetFolderForOwnerAsync(string configuredRoot) =>
        _isSelfProfile
            ? _friendService.SetSelfProfileFolderAsync(
                _category,
                configuredRoot)
            : _friendService.SetFolderAsync(
                _ownerId,
                _category,
                configuredRoot);

    private string EnsureCharacterVariantFoldersForOwner() =>
        _isSelfProfile
            ? _friendService.EnsureSelfProfileCharacterVariantFolders()
            : _friendService.EnsureCharacterVariantFolders(_ownerId);

    private Task RemoveCardFromOwnerAsync(string filePath) =>
        _isSelfProfile
            ? _friendService.RemoveSelfProfileCardAsync(filePath)
            : _friendService.RemoveCardAsync(_ownerId, filePath);

    private Task RenameOwnerAsync(string name) =>
        _isSelfProfile
            ? _friendService.RenameSelfProfileAsync(name)
            : _friendService.RenameAsync(_ownerId, name);

    private Task ClearFolderForOwnerAsync() =>
        _isSelfProfile
            ? _friendService.ClearSelfProfileFolderAsync(_category)
            : _friendService.ClearFolderAsync(_ownerId, _category);

    private string GetOwnerString(string friendKey, string selfProfileKey) =>
        ResLoader.GetString(_isSelfProfile ? selfProfileKey : friendKey);

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private void RunFriendAction(string operation, Func<Task> action) =>
        UiEventGuard.Run(
            _logger,
            operation,
            action,
            _ => ShowStatus(
                ResLoader.GetString("FriendDetail_OperationFailed"),
                InfoBarSeverity.Error));
}

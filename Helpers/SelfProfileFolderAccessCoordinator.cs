using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Helpers;

internal sealed class SelfProfileFolderAccessCoordinator
{
    private sealed record CategoryOptions(
        CardType CardType,
        string Header,
        IReadOnlyList<string> Paths);

    private static readonly ResourceLoader ResLoader = new();

    private readonly FriendService _friendService;
    private readonly SettingsViewModel _settingsViewModel;

    public SelfProfileFolderAccessCoordinator(
        FriendService friendService,
        SettingsViewModel settingsViewModel)
    {
        _friendService = friendService;
        _settingsViewModel = settingsViewModel;
    }

    public CardType? GetFirstAvailableCategory()
    {
        foreach (var cardType in SupportedCardTypes)
        {
            if (HasExistingBoundFolder(cardType))
                return cardType;
        }

        return null;
    }

    public bool HasExistingBoundFolder(CardType cardType) =>
        _friendService.GetFolder(
                _friendService.SelfProfile,
                cardType) is { } folderPath
            && Directory.Exists(folderPath);

    public async Task<bool> EnsureAnyFolderAsync(XamlRoot xamlRoot)
    {
        if (GetFirstAvailableCategory() is not null)
            return true;

        var existingOptions = GetExistingFolderOptions();
        if (existingOptions.Count > 0)
        {
            var selected = await SelectExistingFoldersAsync(
                xamlRoot,
                existingOptions,
                "SelfProfileExistingFolderDialog");
            if (selected is null)
                return false;

            await _friendService.BindExistingSelfProfileFoldersAsync(
                ToBindings(selected));
            return GetFirstAvailableCategory() is not null;
        }

        var creationOptions = GetCreatableRootOptions();
        if (creationOptions.Count == 0)
        {
            await ShowUnavailableMessageAsync(xamlRoot);
            return false;
        }

        var roots = await ConfirmFolderCreationAsync(
            xamlRoot,
            creationOptions,
            ResLoader.GetString("SelfProfile_CreateFoldersDialogMessage"),
            "SelfProfileCreateFolderDialog");
        if (roots is null)
            return false;

        await _friendService.CreateSelfProfileFoldersAsync(roots);
        return GetFirstAvailableCategory() is not null;
    }

    public async Task<bool> EnsureCategoryFolderAsync(
        CardType cardType,
        XamlRoot xamlRoot)
    {
        if (HasExistingBoundFolder(cardType))
            return true;

        var existingOptions = GetExistingFolderOptions(cardType);
        if (existingOptions.Count > 0)
        {
            var selected = await SelectExistingFoldersAsync(
                xamlRoot,
                existingOptions,
                "SelfProfileExistingCategoryFolderDialog");
            if (selected is null)
                return false;

            await _friendService.BindExistingSelfProfileFoldersAsync(
                ToBindings(selected));
            return HasExistingBoundFolder(cardType);
        }

        var creationOptions = GetCreatableRootOptions(cardType);
        if (creationOptions.Count == 0)
        {
            await ShowUnavailableMessageAsync(xamlRoot);
            return false;
        }

        var roots = await ConfirmFolderCreationAsync(
            xamlRoot,
            creationOptions,
            string.Format(
                ResLoader.GetString(
                    "SelfProfile_CreateCategoryFolderDialogMessage"),
                GetCategoryHeader(cardType)),
            "SelfProfileCreateCategoryFolderDialog");
        if (roots is null)
            return false;

        await _friendService.CreateSelfProfileFoldersAsync(roots);
        return HasExistingBoundFolder(cardType);
    }

    private IReadOnlyList<CategoryOptions> GetExistingFolderOptions(
        CardType? onlyCardType = null)
    {
        var result = new List<CategoryOptions>();
        foreach (var cardType in SupportedCardTypes)
        {
            if (onlyCardType is not null && cardType != onlyCardType)
                continue;

            var candidates = GetConfiguredRoots(cardType)
                .Select(FriendFolderLayout.FindExistingSelfFolderPath)
                .Where(path => path is not null)
                .Select(path => path!)
                .Where(path => _friendService.FindFolderOwner(
                    path,
                    _friendService.SelfProfile.Id) is null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count > 0)
            {
                result.Add(new CategoryOptions(
                    cardType,
                    GetCategoryHeader(cardType),
                    candidates));
            }
        }

        return result;
    }

    private IReadOnlyList<CategoryOptions> GetCreatableRootOptions(
        CardType? onlyCardType = null)
    {
        var result = new List<CategoryOptions>();
        foreach (var cardType in SupportedCardTypes)
        {
            if (onlyCardType is not null && cardType != onlyCardType)
                continue;

            var roots = GetConfiguredRoots(cardType)
                .Where(root =>
                {
                    var path = FriendFolderLayout.FindExistingSelfFolderPath(
                            root)
                        ?? FriendFolderLayout.GetSelfFolderPath(root);
                    return _friendService.FindFolderOwner(
                        path,
                        _friendService.SelfProfile.Id) is null;
                })
                .ToList();
            if (roots.Count > 0)
            {
                result.Add(new CategoryOptions(
                    cardType,
                    GetCategoryHeader(cardType),
                    roots));
            }
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<CardType, string>?>
        SelectExistingFoldersAsync(
            XamlRoot xamlRoot,
            IReadOnlyList<CategoryOptions> options,
            string automationId)
    {
        if (options.All(option => option.Paths.Count == 1))
        {
            return options.ToDictionary(
                option => option.CardType,
                option => option.Paths[0]);
        }

        var (panel, selectors) = CreatePathSelectionPanel(
            options,
            ResLoader.GetString(
                "SelfProfile_SelectExistingFoldersDialogMessage"),
            automationId);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = ResLoader.GetString(
                "SelfProfile_SelectExistingFoldersDialogTitle"),
            Content = panel,
            PrimaryButtonText = ResLoader.GetString(
                "SelfProfile_BindAndOpenButton"),
            CloseButtonText = ResLoader.GetString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        AutomationProperties.SetAutomationId(dialog, automationId);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return GetSelectedPaths(options, selectors);
    }

    private async Task<FriendFolderRoots?> ConfirmFolderCreationAsync(
        XamlRoot xamlRoot,
        IReadOnlyList<CategoryOptions> options,
        string message,
        string automationId)
    {
        var (panel, selectors) = CreatePathSelectionPanel(
            options,
            message,
            automationId);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = ResLoader.GetString(
                "SelfProfile_CreateFoldersDialogTitle"),
            Content = panel,
            PrimaryButtonText = ResLoader.GetString(
                "SelfProfile_CreateAndOpenButton"),
            CloseButtonText = ResLoader.GetString(
                "SelfProfile_NotNowButton"),
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(dialog, automationId);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        var selected = GetSelectedPaths(options, selectors);
        return new FriendFolderRoots(
            selected.GetValueOrDefault(CardType.Scene),
            selected.GetValueOrDefault(CardType.Character),
            selected.GetValueOrDefault(CardType.Coordinate));
    }

    private async Task ShowUnavailableMessageAsync(XamlRoot xamlRoot)
    {
        var hasConfiguredRoots = SupportedCardTypes.Any(cardType =>
            GetConfiguredRoots(cardType).Count > 0);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = ResLoader.GetString(
                hasConfiguredRoots
                    ? "SelfProfile_FolderConflictDialogTitle"
                    : "SelfProfile_NoConfiguredFoldersDialogTitle"),
            Content = ResLoader.GetString(
                hasConfiguredRoots
                    ? "SelfProfile_FolderConflictDialogMessage"
                    : "SelfProfile_NoConfiguredFoldersDialogMessage"),
            CloseButtonText = ResLoader.GetString("Common_OK"),
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(
            dialog,
            hasConfiguredRoots
                ? "SelfProfileFolderConflictDialog"
                : "SelfProfileNoConfiguredFolderDialog");
        await dialog.ShowAsync();
    }

    private static (
        StackPanel Panel,
        IReadOnlyDictionary<CardType, ComboBox> Selectors)
        CreatePathSelectionPanel(
            IReadOnlyList<CategoryOptions> options,
            string message,
            string automationId)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
        });
        var selectors = new Dictionary<CardType, ComboBox>();
        foreach (var option in options)
        {
            if (option.Paths.Count == 1)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = string.Format(
                        ResLoader.GetString("Friends_ConfiguredPathSummary"),
                        option.Header,
                        option.Paths[0]),
                    TextWrapping = TextWrapping.Wrap,
                });
                continue;
            }

            var selector = new ComboBox
            {
                Header = option.Header,
                ItemsSource = option.Paths,
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetAutomationId(
                selector,
                $"{automationId}_{option.CardType}ComboBox");
            selectors.Add(option.CardType, selector);
            panel.Children.Add(selector);
        }

        return (panel, selectors);
    }

    private static IReadOnlyDictionary<CardType, string> GetSelectedPaths(
        IReadOnlyList<CategoryOptions> options,
        IReadOnlyDictionary<CardType, ComboBox> selectors) =>
        options.ToDictionary(
            option => option.CardType,
            option => option.Paths.Count == 1
                ? option.Paths[0]
                : selectors[option.CardType].SelectedItem as string
                    ?? option.Paths[0]);

    private static SelfProfileFolderBindings ToBindings(
        IReadOnlyDictionary<CardType, string> selected) =>
        new(
            selected.GetValueOrDefault(CardType.Scene),
            selected.GetValueOrDefault(CardType.Character),
            selected.GetValueOrDefault(CardType.Coordinate));

    private IReadOnlyList<string> GetConfiguredRoots(CardType cardType)
    {
        IEnumerable<string> roots = cardType switch
        {
            CardType.Character => _settingsViewModel.CharacterFolderPaths,
            CardType.Coordinate => _settingsViewModel.CoordinateFolderPaths,
            _ => _settingsViewModel.FolderPaths,
        };
        return roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetCategoryHeader(CardType cardType) =>
        ResLoader.GetString(cardType switch
        {
            CardType.Character => "Friends_CharacterPathHeader",
            CardType.Coordinate => "Friends_CoordinatePathHeader",
            _ => "Friends_ScenePathHeader",
        });

    private static CardType[] SupportedCardTypes { get; } =
    [
        CardType.Scene,
        CardType.Character,
        CardType.Coordinate,
    ];
}

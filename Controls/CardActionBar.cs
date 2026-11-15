using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The detail pages' file actions as visible buttons: open the folder, copy the
/// file, copy the path, and the rest behind "⋯".
/// </summary>
/// <remarks>
/// These used to be gestures nobody could see: a tap on the path text copied
/// it, a right-click on it copied the folder, a right-click on a pixiv or
/// BepisDB link copied the link. The gestures still work; this is the way to
/// find them. Shared by the scene, character and outfit pages so each offers
/// the same actions in the same order.
/// </remarks>
public sealed partial class CardActionBar : StackPanel
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(CardActionBar), new PropertyMetadata(null));

    public static readonly DependencyProperty PixivUrlProperty = DependencyProperty.Register(
        nameof(PixivUrl), typeof(string), typeof(CardActionBar), new PropertyMetadata(null));

    public static readonly DependencyProperty BepisDbUrlProperty = DependencyProperty.Register(
        nameof(BepisDbUrl), typeof(string), typeof(CardActionBar), new PropertyMetadata(null));

    public CardActionBar()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 4;
        Children.Add(Action("", "CardAction_OpenFolder", "CardActionOpenFolder",
            () => FilePath is { } path ? CardFileActions.OpenFolderAsync(path) : Task.CompletedTask));
        Children.Add(Action("", "CardAction_CopyFile", "CardActionCopyFile",
            () => FilePath is { } path ? CardFileActions.CopyFilesAsync([path], "Detail.CopyFile") : Task.CompletedTask));

        var more = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 },
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Padding = new Thickness(8, 6, 8, 6),
        };
        AutomationProperties.SetName(more, UiText.Get("CardAction_More"));
        AutomationProperties.SetAutomationId(more, "CardActionMore");
        ToolTipService.SetToolTip(more, UiText.Get("CardAction_More"));
        var menu = new MenuFlyout();
        menu.Opening += (_, _) => BuildMore(menu);
        more.Flyout = menu;
        Children.Add(more);
    }

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public string? PixivUrl
    {
        get => (string?)GetValue(PixivUrlProperty);
        set => SetValue(PixivUrlProperty, value);
    }

    public string? BepisDbUrl
    {
        get => (string?)GetValue(BepisDbUrlProperty);
        set => SetValue(BepisDbUrlProperty, value);
    }

    private static Button Action(string glyph, string labelKey, string automationId, Func<Task> run)
    {
        var label = UiText.Get(labelKey);
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Padding = new Thickness(8, 6, 10, 6),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = glyph, FontSize = 14 },
                    new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) =>
            UiEventGuard.Run(App.Services.GetRequiredService<Services.IAppLogger>(), "Detail.Action", run);
        return button;
    }

    private void BuildMore(MenuFlyout menu)
    {
        menu.Items.Clear();
        if (FilePath is not { } path) return;
        AddState(menu, path, CardFlag.Queued, "\uE710", "CardAction_Queue");
        AddState(menu, path, CardFlag.Liked, "\uEB51", "CardAction_Like");
        AddState(menu, path, CardFlag.Hidden, "\uED1A", "CardAction_Hide");
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(menu, "", "CardAction_CopyPath", () => CardFileActions.CopyPath(path, "Detail.CopyFilePath"));
        if (Path.GetDirectoryName(path) is { } folder)
            Add(menu, "", "CardAction_CopyFolder", () => DetailNavigationHelper.CopyText(folder));
        if (PixivUrl is { } pixiv)
            Add(menu, "", "CardAction_CopyPixiv", () => CardFileActions.CopyLink(pixiv, path, "Detail.CopyPixivUrl"));
        if (BepisDbUrl is { } bepis)
            Add(menu, "", "CardAction_CopyBepisDb", () => CardFileActions.CopyLink(bepis, path, "Detail.CopyBepisDbUrl"));
    }

    private static void Add(MenuFlyout menu, string glyph, string key, Action run)
    {
        var item = new MenuFlyoutItem { Text = UiText.Get(key), Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => run();
        menu.Items.Add(item);
    }

    private static void AddState(MenuFlyout menu, string path, CardFlag flag, string glyph, string automationId)
    {
        var item = new MenuFlyoutItem
        {
            Text = CardStateActions.Label(path, flag),
            Icon = new FontIcon { Glyph = glyph },
        };
        AutomationProperties.SetAutomationId(item, automationId);
        item.Click += (_, _) => _ = CardStateActions.ToggleAsync([path], flag);
        menu.Items.Add(item);
    }
}

using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class UsageHistoryPage : Page
{
    private readonly UsageRecordService _records = App.Services.GetRequiredService<UsageRecordService>();
    private readonly IAppLogger _logger = App.Services.GetRequiredService<IAppLogger>();
    private readonly UsageHistoryViewModel _viewModel = new(
        App.Services.GetRequiredService<UsageRecordService>(),
        App.Services.GetRequiredService<LibraryRegistry>());

    public UsageHistoryPage()
    {
        InitializeComponent();
        HistoryList.ItemsSource = _viewModel.Rows;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _records.Recorded += OnRecorded;
        Reload();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _records.Recorded -= OnRecorded;
        base.OnNavigatedFrom(e);
    }

    // The service writes on a background thread, so hop back before touching the list.
    private void OnRecorded() => DispatcherQueue.TryEnqueue(Reload);

    private void Reload() => UiEventGuard.Run(_logger, "Usage.Load", async () =>
    {
        await _viewModel.LoadAsync();
        UpdateChrome();
    });

    private void UpdateChrome()
    {
        EmptyPanel.Visibility = _viewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        Subtitle.Text = UiText.Format("Usage_Count", _viewModel.Rows.Count);
    }

    /// <summary>Opens the page the card came from. Local cards never have one.</summary>
    private void Row_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not UsageHistoryRow row || row.Record.OriginUrl is not { } url) return;
        UiEventGuard.Run(_logger, "Usage.OpenOrigin",
            async () => await Windows.System.Launcher.LaunchUriAsync(new Uri(url)));
    }

    /// <summary>
    /// Exports through the clipboard rather than a file picker: a picker needs
    /// the window handle, which only the settings view model owns today, and the
    /// log is a short tab-separated list that pastes straight into anything.
    /// </summary>
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Rows.Count == 0) return;
        DetailNavigationHelper.CopyText(_viewModel.ToPlainText());
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => UiEventGuard.Run(
        _logger, "Usage.Clear", async () =>
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = UiText.Get("Usage_ClearConfirmTitle"),
                Content = UiText.Get("Usage_ClearConfirmBody"),
                PrimaryButtonText = UiText.Get("Usage_ClearConfirmYes"),
                CloseButtonText = UiText.Get("Usage_ClearConfirmNo"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            await _viewModel.ClearAsync();
            UpdateChrome();
        });
}

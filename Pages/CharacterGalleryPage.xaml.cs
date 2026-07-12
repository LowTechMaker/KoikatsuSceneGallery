using System.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class CharacterGalleryPage : Page
{
    public CharacterGalleryViewModel ViewModel { get; }

    private readonly List<WeakReference<TextBlock>> _fileNameTexts = [];
    private readonly GalleryLayoutEngine _layout;

    public CharacterGalleryPage()
    {
        ViewModel = App.Services.GetRequiredService<CharacterGalleryViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _layout = new GalleryLayoutEngine(352.0 / 252.0, GalleryGrid, DispatcherQueue, ViewModel.SetShuffleDisplayCount, App.Services.GetRequiredService<SettingsViewModel>());
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.ViewRefreshed += () =>
            DispatcherQueue.TryEnqueue(RequestVisibleThumbnails);
        Loaded += (_, _) => _layout.OnLoaded(RequestVisibleThumbnails);
        App.Services.GetRequiredService<SettingsViewModel>().CharacterFolderPathsChanged += OnCharacterFolderPathsChanged;
    }

    private void OnCharacterFolderPathsChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
            ViewModel.LoadCardsCommand.ExecuteAsync(null)
                .Observe(App.Services.GetRequiredService<IAppLogger>(), "CharacterGallery.ReloadFolders"));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "CharacterGallery.Navigate", async () =>
        {
            _layout.ApplyCacheLength();
            _layout.RefreshSizeSelector(SizeButtonsPanel);
            _layout.UpdateSizeButtons(SizeSmallButton, SizeMediumButton, SizeLargeButton);
            if (ViewModel.Cards.Count == 0)
                await ViewModel.LoadCardsCommand.ExecuteAsync(null);
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CharacterGalleryViewModel.ShowFileNames))
        {
            UpdateFileNameVisibility();
            _layout.InvalidateAndRefit();
        }
    }

    private void FileNameText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock tb)
        {
            tb.Visibility = ViewModel.ShowFileNames ? Visibility.Visible : Visibility.Collapsed;
            _fileNameTexts.RemoveAll(wr => !wr.TryGetTarget(out _));
            _fileNameTexts.Add(new WeakReference<TextBlock>(tb));
        }
    }

    private void UpdateFileNameVisibility()
    {
        var visibility = ViewModel.ShowFileNames ? Visibility.Visible : Visibility.Collapsed;
        _fileNameTexts.RemoveAll(wr => !wr.TryGetTarget(out _));
        foreach (var wr in _fileNameTexts)
        {
            if (wr.TryGetTarget(out var tb))
                tb.Visibility = visibility;
        }
    }

    private void GalleryGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (args.Item is CharacterCard recycled)
                ViewModel.ReleaseThumbnail(recycled);
            return;
        }
        args.RegisterUpdateCallback(GalleryGrid_Phase1);
        _layout.EnsureLayoutOnFirstContent();
    }

    private void GalleryGrid_Phase1(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is CharacterCard card)
            ViewModel.RequestThumbnail(card);
    }

    private void SizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag } || !int.TryParse(tag, out int idx))
            return;
        _layout.HandleSizeButtonClick(idx);
        _layout.UpdateSizeButtons(SizeSmallButton, SizeMediumButton, SizeLargeButton);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SearchText = sender.Text;
    }

    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.SelectedSort = Enum.Parse<SortOption>(tag);
            ScrollToTop();
        }
    }

    private void SourceFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceFilterComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.SourceFilter = Enum.Parse<CardSourceFilterOption>(tag);
            ScrollToTop();
        }
    }

    private void SortDirectionButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SortAscending = !SortDirectionButton.IsChecked.GetValueOrDefault();
        SortDirectionIcon.Glyph = ViewModel.SortAscending ? "" : "";
        ScrollToTop();
    }

    private void ScrollToTop()
    {
        if (GalleryGrid is not null && App.Services.GetRequiredService<SettingsViewModel>().ScrollToTopOnSort && ViewModel.CardsView.Count > 0)
            GalleryGrid.ScrollIntoView(ViewModel.CardsView[0]);
    }

    private void GalleryGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<CharacterCard>().Select(c => c.FilePath).ToList();
        if (paths.Count == 0) return;
        e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.Data.SetDataProvider(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var files = new List<IStorageItem>();
                foreach (var path in paths)
                {
                    try { files.Add(await StorageFile.GetFileFromPathAsync(path)); }
                    catch (Exception ex)
                    {
                        App.Services.GetRequiredService<IAppLogger>()
                            .LogError("CharacterGallery.PrepareDragFile", ex, path);
                    }
                }
                request.SetData(files);
            }
            finally { deferral.Complete(); }
        });
    }

    private void GalleryGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CharacterCard card)
            Frame.Navigate(typeof(CharacterDetailPage), card);
    }

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        var card = ViewModel.GetRandomCard();
        if (card != null)
            Frame.Navigate(typeof(CharacterDetailPage), card);
    }

    private void FeelingLucky_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Reshuffle();
        ScrollToTop();
    }

    private void ScrollToTop_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ScrollToTop();
        args.Handled = true;
    }

    private void ScrollToBottom_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.CardsView.Count > 0)
            GalleryGrid.ScrollIntoView(ViewModel.CardsView[^1]);
        args.Handled = true;
    }

    private void RequestVisibleThumbnails()
    {
        if (GalleryGrid.ItemsPanelRoot is not ItemsWrapGrid panel) return;
        int first = panel.FirstVisibleIndex;
        int last = panel.LastVisibleIndex;
        if (first < 0) return;
        for (int i = first; i <= last && i < ViewModel.CardsView.Count; i++)
        {
            if (ViewModel.CardsView[i] is CharacterCard card)
                ViewModel.RequestThumbnail(card);
        }
    }
}

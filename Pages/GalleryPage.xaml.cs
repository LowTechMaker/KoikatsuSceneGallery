using System.ComponentModel;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.System;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class GalleryPage : Page
{
    public GalleryViewModel ViewModel { get; }

    private readonly List<WeakReference<TextBlock>> _fileNameTexts = [];

    public GalleryPage()
    {
        ViewModel = App.GalleryViewModel;
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel.Cards.Count == 0)
            await ViewModel.LoadCardsCommand.ExecuteAsync(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GalleryViewModel.ShowFileNames))
            UpdateFileNameVisibility();
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
        // Generate (or resolve a cached) thumbnail only for cards that scroll
        // into view, instead of the whole library up front.
        if (args.InRecycleQueue) return;
        if (args.Item is SceneCard card)
            ViewModel.RequestThumbnail(card);
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

    private void GameFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameFilterComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.GameFilter = Enum.Parse<GameFilterOption>(tag);
            ScrollToTop();
        }
    }

    private void EnvironmentFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EnvironmentFilterComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.EnvironmentFilter = Enum.Parse<EnvironmentFilterOption>(tag);
            ScrollToTop();
        }
    }

    private void TimelineFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TimelineFilterComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.TimelineFilter = Enum.Parse<TimelineFilterOption>(tag);
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
        if (App.SettingsViewModel.ScrollToTopOnSort && ViewModel.CardsView.Count > 0)
            GalleryGrid.ScrollIntoView(ViewModel.CardsView[0]);
    }

    private async void GalleryGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var files = new List<StorageFile>();
        foreach (var item in e.Items)
        {
            if (item is SceneCard card)
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(card.FilePath);
                    files.Add(file);
                }
                catch (Exception) { }
            }
        }
        if (files.Count > 0)
        {
            e.Data.SetStorageItems(files);
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
    }

    private void GalleryGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SceneCard card)
        {
            Frame.Navigate(typeof(DetailPage), card);
        }
    }

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        var card = ViewModel.GetRandomCard();
        if (card != null)
            Frame.Navigate(typeof(DetailPage), card);
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
}
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Resources;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class ImportPage : Page
{
    private static readonly ResourceLoader ResLoader = new();

    public ImportViewModel ViewModel { get; }

    public ImportPage()
    {
        ViewModel = App.ImportViewModel!;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportViewModel.ShowRejectedWarning) && ViewModel.ShowRejectedWarning)
        {
            RejectedWarningBar.Message = string.Format(
                ResLoader.GetString("Import_RejectedWarningMessage"),
                ViewModel.RejectedCount);
        }
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Import";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items
            .Where(i => i is Windows.Storage.StorageFile)
            .Select(i => i.Path)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (paths.Count > 0)
            await ViewModel.AddFilesCommand.ExecuteAsync(paths);
    }
}

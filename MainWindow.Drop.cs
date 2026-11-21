using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Pages;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace KoikatsuSceneGallery;

/// <summary>
/// Files dragged onto any page are imported: the window takes the drop, opens
/// the import page and hands the files over.
/// </summary>
/// <remarks>
/// Only the import page used to accept files, so dropping a card on the
/// library - the obvious place to put it - did nothing at all. Pages with
/// drop targets of their own (import, local sources) keep them: the window
/// steps aside there.
/// </remarks>
public sealed partial class MainWindow
{
    private void InitializeWindowDrop()
    {
        RootLayout.AllowDrop = true;
        RootLayout.DragOver += RootLayout_DragOver;
        RootLayout.DragLeave += (_, _) => ShowDropOverlay(false);
        RootLayout.Drop += RootLayout_Drop;
    }

    private bool AcceptsWindowDrop(DragEventArgs e) =>
        App.Services.GetService<ImportViewModel>() is not null
        && NavFrame.Content is not (ImportPage or LocalSourcesPage)
        && !Palette.IsOpen
        && e.DataView.Contains(StandardDataFormats.StorageItems)
        && !InternalDrag.IsInternal(e.DataView);

    private void RootLayout_DragOver(object sender, DragEventArgs e)
    {
        // A page that took the drag itself (its own drop zone) has the say.
        if (e.Handled || !AcceptsWindowDrop(e))
        {
            ShowDropOverlay(false);
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = UiText.Get("Drop_Caption");
        e.DragUIOverride.IsCaptionVisible = true;
        ShowDropOverlay(true);
    }

    private void RootLayout_Drop(object sender, DragEventArgs e)
    {
        ShowDropOverlay(false);
        if (e.Handled || !AcceptsWindowDrop(e)) return;
        e.Handled = true;
        var deferral = e.GetDeferral();
        UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Window.Drop", async () =>
        {
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count == 0) return;
                NavFrame.Navigate(typeof(ImportPage), new ImportDropRequest([.. items]));
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private void ShowDropOverlay(bool show)
    {
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (DropOverlay.Visibility != visibility) DropOverlay.Visibility = visibility;
    }
}

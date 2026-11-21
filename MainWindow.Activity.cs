using System.Collections.ObjectModel;
using System.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery;

/// <summary>
/// The title bar's activity button: everything running in the background, in
/// one list, each with its progress and - where it can be - a way to stop it.
/// </summary>
/// <remarks>
/// <para>Each long job used to report in its own way and its own place: the
/// artwork re-fetch in a banner at the bottom right, the author refresh as a
/// ring beside a button on the authors page, the library scan as a caption
/// above the grid, the import as an overlay on the import page. Leaving the
/// page hid most of them, so there was no telling whether anything was still
/// running.</para>
/// <para>Only work that runs for a while is listed. Thumbnails and metadata
/// parsing come and go with every scroll; listing them would make the button
/// flicker rather than inform. How a job ended is a toast, not a lingering row:
/// the list is for what is happening now.</para>
/// </remarks>
public sealed partial class MainWindow
{
    private readonly ObservableCollection<ActivityItem> _activities = [];

    private void InitializeActivity()
    {
        ActivityList.ItemsSource = _activities;
        _activities.CollectionChanged += (_, _) => UpdateActivityButton();
        UpdateActivityButton();

        foreach (var library in App.Services.GetRequiredService<LibraryRegistry>().All)
        {
            var target = library;
            target.ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GalleryViewModelBase.IsLoading))
                    OnUi(() => TrackLibraryScan(target));
            };
            TrackLibraryScan(target);
        }

        var authors = App.Services.GetRequiredService<AuthorsViewModel>();
        authors.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AuthorsViewModel.IsRefreshing) or nameof(AuthorsViewModel.RefreshProgress))
                OnUi(() => TrackAuthorRefresh(authors));
        };

        if (App.Services.GetService<ImportViewModel>() is { } import)
        {
            import.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ImportViewModel.IsImporting) or nameof(ImportViewModel.ExecutionProgress)
                    or nameof(ImportViewModel.TransactionStatusText) or nameof(ImportViewModel.IsAnalyzing)
                    or nameof(ImportViewModel.AnalysisStatusText))
                    OnUi(() => TrackImport(import));
            };
            TrackImport(import);
        }

        if (App.Services.GetService<ArtworkRefetchService>() is { } refetch)
        {
            refetch.PropertyChanged += (_, _) => OnUi(() => TrackRefetch(refetch));
            TrackRefetch(refetch);
        }
    }

    private void OnUi(Action action)
    {
        if (DispatcherQueue.HasThreadAccess) action();
        else DispatcherQueue.TryEnqueue(() => action());
    }

    private ActivityItem Upsert(string id)
    {
        var existing = _activities.FirstOrDefault(item => item.Id == id);
        if (existing is not null) return existing;
        var created = new ActivityItem(id);
        _activities.Add(created);
        return created;
    }

    private void Remove(string id)
    {
        if (_activities.FirstOrDefault(item => item.Id == id) is { } item) _activities.Remove(item);
    }

    private void UpdateActivityButton()
    {
        var count = _activities.Count;
        ActivityButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ActivityRing.IsActive = count > 0;
        ActivityCount.Text = count > 1 ? count.ToString() : "";
        var name = UiText.Format("Activity_ButtonName", count);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ActivityButton, name);
        ToolTipService.SetToolTip(ActivityButton, name);
        // Nothing left to show: a flyout left open would be an empty card.
        if (count == 0) ActivityFlyout.Hide();
    }

    private void TrackLibraryScan(ILibrary library)
    {
        var id = $"scan:{library.Kind}";
        if (!library.ViewModel.IsLoading)
        {
            Remove(id);
            return;
        }

        var item = Upsert(id);
        item.Title = UiText.Format("Activity_LibraryScan", library.Title);
        item.Progress = null;
    }

    private bool _authorRefreshTracked;

    private void TrackAuthorRefresh(AuthorsViewModel authors)
    {
        const string id = "authors";
        if (!authors.IsRefreshing)
        {
            Remove(id);
            if (_authorRefreshTracked)
            {
                _authorRefreshTracked = false;
                AppToast.Show(UiText.Format("Activity_AuthorsDone", authors.RefreshDone, authors.RefreshTotal),
                    InfoBarSeverity.Success, key: id);
            }

            return;
        }

        _authorRefreshTracked = true;
        var item = Upsert(id);
        item.Title = UiText.Get("Activity_AuthorsRefresh");
        item.Detail = authors.RefreshProgress;
        item.Progress = authors.RefreshTotal > 0 ? (double)authors.RefreshDone / authors.RefreshTotal : null;
        item.Cancel = authors.CancelRefreshAll;
    }

    private void TrackImport(ImportViewModel import)
    {
        if (import.IsImporting)
        {
            var item = Upsert("import");
            item.Title = UiText.Get("Activity_Import");
            var counts = import.TransactionProgressCountsText;
            item.Detail = string.Join(" · ", new[] { import.TransactionStatusText, counts }.Where(s => s.Length > 0));
            item.Progress = import.ExecutionProgress is { TotalCount: > 0 } progress
                ? (double)progress.CompletedCount / progress.TotalCount
                : null;
            item.Cancel = () => import.CancelImportCommand.Execute(null);
        }
        else Remove("import");

        if (import.IsAnalyzing)
        {
            var item = Upsert("import-analysis");
            item.Title = UiText.Get("Activity_ImportAnalysis");
            item.Detail = import.AnalysisStatusText;
            item.Progress = import.AnalysisTotalCount > 0
                ? (double)import.AnalysisCompletedCount / import.AnalysisTotalCount
                : null;
        }
        else Remove("import-analysis");
    }

    private ArtworkRefetchPhase? _lastRefetchPhase;

    /// <summary>
    /// The artwork re-fetch, which used to have a banner of its own. Running,
    /// it is a row here; when it ends, how it ended is a toast.
    /// </summary>
    private void TrackRefetch(ArtworkRefetchService refetch)
    {
        const string id = "refetch";
        var phase = refetch.Phase;
        var platform = refetch.ProviderId is { } providerId ? PlatformDisplayName.For(providerId) : "";
        var title = UiText.Format("Refetch_BannerTitle", platform);

        if (refetch.IsBusy)
        {
            var item = Upsert(id);
            item.Title = title;
            item.Detail = phase switch
            {
                ArtworkRefetchPhase.Scanning => UiText.Get("Refetch_Scanning"),
                ArtworkRefetchPhase.BackingUp => UiText.Get("Refetch_BackingUp"),
                ArtworkRefetchPhase.Restoring => UiText.Get("Refetch_Restoring"),
                _ => UiText.Format("Refetch_Progress",
                    refetch.RunDone, refetch.RunTotal, refetch.Updated, refetch.Gone, refetch.Failed),
            };
            item.Progress = phase == ArtworkRefetchPhase.Running && refetch.RunTotal > 0
                ? (double)refetch.RunDone / refetch.RunTotal
                : null;
            // Restoring puts files back; stopping halfway would leave a mix.
            item.Cancel = phase == ArtworkRefetchPhase.Restoring ? null : refetch.Stop;
        }
        else
        {
            Remove(id);
        }

        if (phase == _lastRefetchPhase) return;
        var wasRunning = _lastRefetchPhase is not null && _lastRefetchPhase is not
            (ArtworkRefetchPhase.Completed or ArtworkRefetchPhase.Stopped);
        _lastRefetchPhase = phase;
        if (!wasRunning) return;

        (InfoBarSeverity Severity, string Message)? ending = phase switch
        {
            ArtworkRefetchPhase.Completed => (InfoBarSeverity.Success,
                UiText.Format("Refetch_Completed", refetch.Updated, refetch.Gone, refetch.Failed)),
            ArtworkRefetchPhase.Stopped when refetch.StopReason == ArtworkRefetchStopReason.TooManyFailures =>
                (InfoBarSeverity.Warning,
                    UiText.Format("Refetch_TooManyFailures", ArtworkRefetchPolicy.MaxConsecutiveFailures)),
            ArtworkRefetchPhase.Stopped when refetch.StopReason == ArtworkRefetchStopReason.BackupFailed =>
                (InfoBarSeverity.Error, UiText.Get("Refetch_BackupFailed")),
            ArtworkRefetchPhase.Stopped when refetch.StopReason == ArtworkRefetchStopReason.Error =>
                (InfoBarSeverity.Error, UiText.Get("Refetch_Error")),
            // Stopped by the user: they know, and the old banner said nothing either.
            _ => null,
        };
        if (ending is { } end) AppToast.Show(end.Message, end.Severity, title, key: id, sticky: true);
    }

    private void ActivityCancel_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ActivityItem { Cancel: { } cancel })
            cancel();
    }
}

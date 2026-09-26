using System.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The "fetch artwork data again" section of a plugin's settings dialog.
/// </summary>
/// <remarks>
/// Scan first, then agree, then run: the number of artworks and the hours it
/// will take are shown before anything is sent, and the start button stays
/// disabled until the warning has been acknowledged. The run itself belongs
/// to <see cref="ArtworkRefetchService"/>, so closing the dialog does not stop
/// it; reopening shows it where it is.
/// </remarks>
internal sealed partial class ArtworkRefetchPanel : StackPanel
{
    private readonly ArtworkRefetchService _service;
    private readonly string _providerId;
    private readonly string _platform;

    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _unassigned = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
    };
    private readonly ProgressBar _progress = new() { Minimum = 0 };
    private readonly CheckBox _acknowledge = new();
    private readonly Button _scan = new();
    private readonly Button _scanRestart = new();
    // Missing-only first and accented: it overwrites nothing, so it is the
    // one to reach for; a full re-fetch is the deliberate second choice.
    private readonly Button _startMissing = new() { Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly StackPanel _backupSection = new() { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _backupList = new() { Spacing = 4 };
    // Backups are listed from disk, and Render runs for every artwork a run
    // finishes. Re-read only when the phase changes, which is when they can.
    private ArtworkRefetchPhase? _backupsListedIn;

    public ArtworkRefetchPanel(ArtworkRefetchService service, string providerId, string platform)
    {
        _service = service;
        _providerId = providerId;
        _platform = platform;
        Spacing = 8;

        Children.Add(new TextBlock
        {
            Text = UiText.Get("Refetch_SectionTitle"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        Children.Add(new TextBlock
        {
            Text = UiText.Format("Refetch_Description", platform),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
        });
        Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = InfoBarSeverity.Error,
            Title = UiText.Get("Refetch_WarningTitle"),
            Message = UiText.Format("Refetch_WarningMessage", platform),
        });
        Children.Add(_status);
        Children.Add(_progress);
        Children.Add(_unassigned);

        _acknowledge.Content = new TextBlock
        {
            Text = UiText.Get("Refetch_Acknowledge"),
            TextWrapping = TextWrapping.Wrap,
        };
        _acknowledge.Checked += (_, _) => Render();
        _acknowledge.Unchecked += (_, _) => Render();
        Children.Add(_acknowledge);

        _scanRestart.Content = UiText.Get("Refetch_ScanRestart");
        _stop.Content = UiText.Get("Refetch_Stop");
        _scan.Click += (_, _) => Scan(resume: _service.HasUnfinishedRun(_providerId));
        _scanRestart.Click += (_, _) => Scan(resume: false);
        _startMissing.Click += (_, _) =>
        {
            _acknowledge.IsChecked = false;
            _service.Start(_providerId, ArtworkRefetchScope.MissingOnly);
        };
        _start.Click += (_, _) =>
        {
            _acknowledge.IsChecked = false;
            _service.Start(_providerId, ArtworkRefetchScope.All);
        };
        _stop.Click += (_, _) => _service.Stop();
        Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _scan, _scanRestart, _startMissing, _start, _stop },
        });

        _backupSection.Children.Add(new TextBlock
        {
            Text = UiText.Get("Refetch_BackupsTitle"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        _backupSection.Children.Add(new TextBlock
        {
            Text = UiText.Format("Refetch_BackupsHint", Services.SidecarBackupStore.Slots),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
        });
        _backupSection.Children.Add(_backupList);
        Children.Add(_backupSection);

        // Not Loaded/Unloaded: a ContentDialog re-parents its content into a
        // popup as it opens, and the Unloaded that raises can arrive after the
        // Loaded — which left this panel unsubscribed, deaf to a scan that
        // had in fact finished. The owner detaches it when the dialog closes.
        _service.PropertyChanged += OnServiceChanged;
        Render();
    }

    /// <summary>Stops following the service. Call when the dialog closes.</summary>
    public void Detach() => _service.PropertyChanged -= OnServiceChanged;

    private void Scan(bool resume)
    {
        _acknowledge.IsChecked = false;
        _service.ScanAsync(_providerId, resume).Observe(
            App.Services.GetRequiredService<IAppLogger>(), "ArtworkRefetch.Scan");
    }

    private void OnServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) Render();
        else DispatcherQueue.TryEnqueue(Render);
    }

    private void Render()
    {
        var mine = string.Equals(_service.ProviderId, _providerId, StringComparison.OrdinalIgnoreCase);
        var phase = mine ? _service.Phase : ArtworkRefetchPhase.Idle;
        var otherBusy = !mine && _service.IsBusy;
        var unfinished = _service.HasUnfinishedRun(_providerId);

        _scan.Content = UiText.Get(unfinished ? "Refetch_ScanResume" : "Refetch_Scan");
        Show(_scan, !_service.IsBusy);
        Show(_scanRestart, !_service.IsBusy && unfinished);
        // A restore cannot be stopped half way; see ArtworkRefetchService.RestoreAsync.
        Show(_stop, mine && _service.IsBusy && phase != ArtworkRefetchPhase.Restoring);
        Show(_acknowledge, phase == ArtworkRefetchPhase.Scanned && _service.Total > 0);
        var scanned = phase == ArtworkRefetchPhase.Scanned && _service.Total > 0;
        var resuming = _service.ScannedForResume;
        var acknowledged = _acknowledge.IsChecked == true;
        // Resuming continues whichever kind of run was started, so it is one
        // button; from the start, the choice between the two is offered.
        Show(_startMissing, scanned && !resuming && _service.Missing > 0);
        _startMissing.Content = UiText.Format("Refetch_StartMissing", _service.Missing);
        _startMissing.IsEnabled = acknowledged;
        Show(_start, scanned);
        _start.Content = resuming
            ? UiText.Get("Refetch_Continue")
            : UiText.Format("Refetch_StartAll", _service.Total - _service.KnownGone);
        _start.IsEnabled = acknowledged && _service.Total - _service.Skipped > 0;
        _scan.IsEnabled = _scanRestart.IsEnabled = !otherBusy;

        Show(_progress, phase is ArtworkRefetchPhase.Scanning or ArtworkRefetchPhase.BackingUp
            or ArtworkRefetchPhase.Running or ArtworkRefetchPhase.Restoring);
        _progress.IsIndeterminate = phase != ArtworkRefetchPhase.Running;
        _progress.Maximum = Math.Max(1, _service.RunTotal);
        _progress.Value = _service.RunDone;

        _status.Text = otherBusy
            ? UiText.Get("Refetch_OtherPlatformBusy")
            : phase switch
            {
                ArtworkRefetchPhase.Scanning => UiText.Get("Refetch_Scanning"),
                ArtworkRefetchPhase.BackingUp => UiText.Get("Refetch_BackingUp"),
                ArtworkRefetchPhase.Restoring => UiText.Get("Refetch_Restoring"),
                ArtworkRefetchPhase.Scanned => ScanSummary(),
                ArtworkRefetchPhase.Running => Progress(),
                ArtworkRefetchPhase.Completed => UiText.Format("Refetch_Completed", _service.Updated, _service.Gone, _service.Failed),
                ArtworkRefetchPhase.Stopped => StoppedText(),
                _ when _service.LastRestore is { } restore && mine => UiText.Format("Refetch_Restored",
                    Stamp(restore.BackupCreatedAt), restore.Restored, restore.Removed, restore.Failed),
                _ => unfinished ? UiText.Get("Refetch_UnfinishedHint") : "",
            };
        Show(_status, !string.IsNullOrEmpty(_status.Text));

        _unassigned.Text = phase is ArtworkRefetchPhase.Scanned or ArtworkRefetchPhase.Running && _service.Unassigned > 0
            ? UiText.Format("Refetch_Unassigned", _service.Unassigned)
            : "";
        Show(_unassigned, !string.IsNullOrEmpty(_unassigned.Text));

        RenderBackups(phase);
    }

    private void RenderBackups(ArtworkRefetchPhase phase)
    {
        if (_backupsListedIn == phase && _backupList.Children.Count > 0) return;
        _backupsListedIn = phase;

        _backupList.Children.Clear();
        var backups = _service.Backups(_providerId);
        Show(_backupSection, backups.Count > 0);
        foreach (var backup in backups)
            _backupList.Children.Add(BackupRow(backup));
    }

    private FrameworkElement BackupRow(Services.SidecarBackup backup)
    {
        var confirm = new Button
        {
            Content = UiText.Get("Refetch_RestoreConfirmButton"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 12,
                MaxWidth = 320,
                Children =
                {
                    new TextBlock
                    {
                        Text = UiText.Format("Refetch_RestoreConfirm", Stamp(backup.CreatedAt)),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    confirm,
                },
            },
        };
        confirm.Click += (_, _) =>
        {
            flyout.Hide();
            _service.RestoreAsync(backup).Observe(
                App.Services.GetRequiredService<IAppLogger>(), "ArtworkRefetch.Restore");
        };

        var restore = new Button
        {
            Content = UiText.Get("Refetch_Restore"),
            Flyout = flyout,
            IsEnabled = !_service.IsBusy,
        };

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = UiText.Format("Refetch_BackupItem", Stamp(backup.CreatedAt), backup.Saved + backup.Absent),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(restore, 1);
        row.Children.Add(restore);
        return row;
    }

    private static string Stamp(DateTimeOffset time)
        => time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture);

    private string ScanSummary()
    {
        if (_service.Total == 0) return UiText.Format("Refetch_NothingFound", _platform);

        if (_service.ScannedForResume)
        {
            var pending = _service.Total - _service.Skipped;
            var (resumeMin, resumeMax) = ArtworkRefetchPolicy.EstimatedHours(pending);
            var kind = UiText.Get(_service.UnfinishedScope(_providerId) == ArtworkRefetchScope.MissingOnly
                ? "Refetch_KindMissing"
                : "Refetch_KindAll");
            return UiText.Format("Refetch_ResumeSummary", kind, _service.Skipped, pending, $"{resumeMin:0.#}", $"{resumeMax:0.#}");
        }

        var (min, max) = ArtworkRefetchPolicy.EstimatedHours(_service.Total - _service.KnownGone);
        var summary = UiText.Format("Refetch_ScanSummary", _service.Total, _service.CardCount, $"{min:0.#}", $"{max:0.#}");
        if (_service.KnownGone > 0)
            summary += " " + UiText.Format("Refetch_ScanGone", _service.KnownGone);
        if (_service.Missing == 0) return summary;
        var (missingMin, missingMax) = ArtworkRefetchPolicy.EstimatedHours(_service.Missing);
        return summary + " " + UiText.Format("Refetch_ScanMissing", _service.Missing, $"{missingMin:0.#}", $"{missingMax:0.#}");
    }

    private string Progress()
        => UiText.Format("Refetch_Progress", _service.RunDone, _service.RunTotal, _service.Updated, _service.Gone, _service.Failed);

    private string StoppedText()
    {
        var progress = _service.RunTotal > 0 ? Progress() + " " : "";
        return progress + _service.StopReason switch
        {
            ArtworkRefetchStopReason.TooManyFailures => UiText.Format("Refetch_TooManyFailures", ArtworkRefetchPolicy.MaxConsecutiveFailures),
            ArtworkRefetchStopReason.BackupFailed => UiText.Get("Refetch_BackupFailed"),
            ArtworkRefetchStopReason.Error => UiText.Get("Refetch_Error"),
            _ => UiText.Get("Refetch_Stopped"),
        };
    }

    private static void Show(UIElement element, bool visible)
        => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
}

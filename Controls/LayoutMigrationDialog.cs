using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The library tidy-up: lists what would move to the three-level layout,
/// moves nothing until the user confirms, and can move everything back.
/// </summary>
/// <remarks>
/// One dialog with three states: the plan, the run, the result. Its own
/// buttons, not the dialog's: their labels and visibility change with the
/// state, each has an AutomationId, and while files move the dialog refuses
/// to close (Escape included) so the run is never left without its stop button.
/// Every decision is <see cref="LibraryLayoutMigration"/>'s; this only shows it.
/// </remarks>
internal static class LayoutMigrationDialog
{
    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        if (App.Services.GetService<LibraryLayoutMigrationService>() is not { } service) return;
        var logger = App.Services.GetRequiredService<IAppLogger>();
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        var notice = new InfoBar { IsClosable = false, IsOpen = false, Severity = InfoBarSeverity.Warning };
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        AutomationProperties.SetAutomationId(summary, "LayoutMigrationSummary");
        var showDetails = new ToggleButton { Content = UiText.Get("LayoutMigration_ShowDetails") };
        var details = new ListView
        {
            MaxHeight = 280,
            SelectionMode = ListViewSelectionMode.None,
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetAutomationId(details, "LayoutMigrationDetails");
        var savePlan = new Button { Content = UiText.Get("LayoutMigration_SavePlan") };
        AutomationProperties.SetAutomationId(savePlan, "LayoutMigrationSavePlan");
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
        var progressText = new TextBlock { Foreground = secondary, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(progressBar, "LayoutMigrationProgress");
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, IsTextSelectionEnabled = true };
        AutomationProperties.SetAutomationId(result, "LayoutMigrationResult");

        var run = new Button { Content = UiText.Get("LayoutMigration_Run"), IsEnabled = false, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        AutomationProperties.SetAutomationId(run, "LayoutMigrationRun");
        var stop = new Button { Content = UiText.Get("LayoutMigration_Stop"), Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(stop, "LayoutMigrationStop");
        var undo = new Button { Content = UiText.Get("LayoutMigration_Undo"), Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(undo, "LayoutMigrationUndo");
        var close = new Button { Content = UiText.Get("LayoutMigration_Close") };
        AutomationProperties.SetAutomationId(close, "LayoutMigrationClose");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { run, stop, undo, close }) buttons.Children.Add(button);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tools.Children.Add(showDetails);
        tools.Children.Add(savePlan);

        var panel = new StackPanel { Spacing = 12, MinWidth = 480, MaxWidth = 640 };
        var lastRun = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = secondary, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(lastRun, "LayoutMigrationLastRun");
        foreach (var element in new UIElement[] { notice, summary, lastRun, tools, details, progressBar, progressText, result, buttons })
            panel.Children.Add(element);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = UiText.Get("LayoutMigration_Title"),
            Content = new ScrollViewer { Content = panel },
        };

        LayoutMigrationPlan? plan = null;
        IReadOnlyList<string> journals = [];
        CancellationTokenSource? running = null;

        dialog.Closing += (_, args) =>
        {
            // Files are moving: closing would hide the only stop button.
            if (running is not null) args.Cancel = true;
        };
        close.Click += (_, _) => { if (running is null) dialog.Hide(); };
        showDetails.Click += (_, _) =>
            details.Visibility = showDetails.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        void ShowStays(IReadOnlyList<LayoutStay> stays)
        {
            details.ItemsSource = stays
                .Select(s => $"{Relative(s.Root, s.Source)}  —  {ReasonText(s)}")
                .ToList();
        }

        void SetRunning(bool on)
        {
            run.Visibility = on ? Visibility.Collapsed : run.Visibility;
            stop.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            close.IsEnabled = !on;
            undo.IsEnabled = !on;
            savePlan.IsEnabled = !on;
            progressBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            progressText.Visibility = progressBar.Visibility;
        }

        var progress = new Progress<LayoutMigrationProgress>(p =>
        {
            progressBar.Maximum = Math.Max(1, p.Total);
            progressBar.Value = p.Done;
            progressText.Text = UiText.Format("LayoutMigration_Progress", p.Done, p.Total);
        });

        void ShowResult(LayoutMigrationResult outcome, bool undoing)
        {
            run.Visibility = Visibility.Collapsed;
            lastRun.Visibility = Visibility.Collapsed;
            showDetails.IsChecked = outcome.Stayed.Count > 0;
            details.Visibility = outcome.Stayed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            savePlan.Visibility = Visibility.Collapsed;
            ShowStays(outcome.Stayed);
            result.Visibility = Visibility.Visible;
            result.Text = undoing
                ? outcome.Stayed.Count == 0 && !outcome.Stopped
                    ? UiText.Format("LayoutMigration_UndoDone", outcome.Moved)
                    : UiText.Format("LayoutMigration_UndoPartial", outcome.Moved, outcome.Stayed.Count)
                : outcome.Stopped
                    ? UiText.Format("LayoutMigration_Stopped", outcome.Moved)
                    : UiText.Format("LayoutMigration_Done", outcome.Moved, outcome.Stayed.Count);
            journals = outcome.Journals;
            undo.Visibility = !undoing && journals.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        async Task RunAsync(Func<CancellationToken, Task<LayoutMigrationResult>> work, bool undoing)
        {
            if (service.BusyReason() is { } busy)
            {
                notice.Message = UiText.Get(busy);
                notice.IsOpen = true;
                return;
            }
            running = new CancellationTokenSource();
            SetRunning(true);
            try
            {
                ShowResult(await work(running.Token), undoing);
            }
            catch (InvalidOperationException ex) when (ex.Message == "LayoutMigration_AppBusy")
            {
                notice.Message = UiText.Get(ex.Message);
                notice.IsOpen = true;
            }
            catch (Exception ex)
            {
                logger.LogError("LayoutMigration.Run", ex);
                result.Visibility = Visibility.Visible;
                result.Text = UiText.Format("LayoutMigration_ReasonFailed", ex.Message);
            }
            finally
            {
                running.Dispose();
                running = null;
                SetRunning(false);
            }
        }

        run.Click += async (_, _) =>
        {
            if (plan is { Moves.Count: > 0 } ready)
                await RunAsync(ct => service.ApplyAsync(ready, progress, ct), undoing: false);
        };
        stop.Click += (_, _) => running?.Cancel();
        undo.Click += async (_, _) =>
        {
            var toUndo = journals;
            if (toUndo.Count > 0)
                await RunAsync(ct => service.RollbackAsync(toUndo, progress, ct), undoing: true);
        };

        savePlan.Click += async (_, _) =>
        {
            if (plan is null) return;
            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "layout-migration-plan" };
                picker.FileTypeChoices.Add("Text", new List<string> { ".txt" });
                WinRT.Interop.InitializeWithWindow.Initialize(picker,
                    Microsoft.UI.Win32Interop.GetWindowFromWindowId(xamlRoot.ContentIslandEnvironment.AppWindowId));
                var file = await picker.PickSaveFileAsync();
                if (file is not null) await File.WriteAllTextAsync(file.Path, plan.ToText());
            }
            catch (Exception ex)
            {
                logger.LogError("LayoutMigration.SavePlan", ex);
            }
        };

        dialog.Opened += async (_, _) =>
        {
            summary.Text = UiText.Get("LayoutMigration_Scanning");
            try
            {
                var unfinished = await service.FindUnfinishedAsync();
                var rollbacks = unfinished.Where(LibraryLayoutMigration.WasBeingUndone).ToList();
                if (unfinished.Count > 0)
                {
                    notice.Message = UiText.Get("LayoutMigration_Resume");
                    notice.IsOpen = true;
                }
                // The last run of each root stays undoable after the dialog was
                // closed, whether or not anything is left to move. Only the
                // newest per root: an older run is never offered over a newer one.
                var undoable = await service.FindUndoableAsync();
                if (undoable.Count > 0)
                {
                    var newest = undoable[^1];
                    lastRun.Text = UiText.Format("LayoutMigration_LastRun",
                        (newest.At ?? DateTimeOffset.Now).LocalDateTime.ToString("g"),
                        undoable.Sum(r => r.Moved));
                    lastRun.Visibility = Visibility.Visible;
                }
                // A rollback that stopped is finished by rolling back, not by migrating.
                journals = [.. undoable.Select(r => r.Journal).Concat(rollbacks).Distinct(StringComparer.OrdinalIgnoreCase)];
                undo.Visibility = journals.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                plan = await service.PlanAsync(CancellationToken.None);
                summary.Text = Summary(plan);
                details.ItemsSource = plan.Moves
                    .Select(m => $"{Relative(m.Root, m.Source)}  →  {Relative(m.Root, m.Destination)}")
                    .Concat(plan.Stays.Select(s => $"{Relative(s.Root, s.Source)}  —  {ReasonText(s)}"))
                    .ToList();
                run.IsEnabled = plan.Moves.Count > 0 && rollbacks.Count == 0;
            }
            catch (Exception ex)
            {
                logger.LogError("LayoutMigration.Plan", ex);
                summary.Text = UiText.Format("LayoutMigration_ReasonFailed", ex.Message);
            }
        };

        await dialog.ShowAsync();
    }

    private static string Summary(LayoutMigrationPlan plan)
    {
        var lines = new List<string>();
        switch (plan.SummaryKind)
        {
            case LayoutMigrationSummaryKind.NothingToDo:
                return UiText.Get("LayoutMigration_NothingToDo");
            case LayoutMigrationSummaryKind.NothingToMoveButLeft:
                // Nothing can move, but the library is not all in the new layout:
                // say what is left and why (the string ends with a colon).
                lines.Add(UiText.Format("LayoutMigration_NothingToMoveButLeft", plan.Stays.Count));
                break;
            default:
                lines.Add(UiText.Format("LayoutMigration_PlanMoves", plan.Moves.Count));
                lines.Add(UiText.Format("LayoutMigration_PlanAuthors", plan.AuthorsMerged));
                lines.Add(UiText.Format("LayoutMigration_PlanRatings", plan.Ratings.Count));
                lines.Add(UiText.Format("LayoutMigration_PlanStays", plan.Stays.Count));
                break;
        }
        foreach (var group in plan.Stays.GroupBy(s => s.Reason).OrderBy(g => g.Key))
            lines.Add($"    {ReasonText(group.First())}: {group.Count()}");
        var outside = plan.OutOfScope.Sum(o => o.FileCount);
        // Files outside the organized folders: the user's own, not an unknown provider's.
        if (outside > 0) lines.Add(UiText.Format("LayoutMigration_OutOfScope", outside));
        return string.Join(Environment.NewLine, lines);
    }

    private static string ReasonText(LayoutStay stay) => stay.Reason switch
    {
        LayoutStayReason.Collision => UiText.Get("LayoutMigration_ReasonCollision"),
        LayoutStayReason.Duplicate => UiText.Get("LayoutMigration_ReasonDuplicate"),
        LayoutStayReason.Loose => UiText.Get("LayoutMigration_ReasonLoose"),
        LayoutStayReason.RatingNotSaved => UiText.Get("LayoutMigration_ReasonRatingNotSaved"),
        LayoutStayReason.Changed => UiText.Get("LayoutMigration_ReasonChanged"),
        _ => UiText.Format("LayoutMigration_ReasonFailed", stay.Detail ?? ""),
    };

    private static string Relative(string root, string path)
        => path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison.Comparison)
            ? Path.GetRelativePath(root, path)
            : path;
}

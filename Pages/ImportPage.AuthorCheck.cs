using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using SceneGallery.PluginSdk;
using Windows.ApplicationModel.DataTransfer;

namespace KoikatsuSceneGallery.Pages;

// Checking an author's posts, one by one, against the cards that have been dropped in.
public sealed partial class ImportPage
{
    private static readonly Brush CautionBrush = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
    private static readonly Brush OrangeBrush = (Brush)Application.Current.Resources["AttentionTextBrush"];


    private bool _authorCheckOpen;
    private string? _lastOfferedAuthorUrl;

    // A copied author address opens the check straight away, and starts it: the user asked
    // for the copy itself to be the request. Each address is offered once, so closing the
    // dialog and coming back to this window does not reopen it.
    private bool TryOfferAuthorCheck(string? text)
    {
        var url = text?.Trim();
        if (string.IsNullOrEmpty(url) || _authorCheckOpen || url == _lastOfferedAuthorUrl) return false;
        if (!ViewModel.CanCheckAuthors || !ViewModel.IsReviewWorkspaceEnabled
            || !ViewModel.HasReviewCards || ViewModel.TryParseAuthorUrl(url) is null)
            return false;

        _lastOfferedAuthorUrl = url;
        UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.AuthorCheck",
            () => ShowAuthorCheckAsync(url, autoStart: true));
        return true;
    }

    private void OpenAuthorCheck_Click(object sender, RoutedEventArgs e)
    {
        ReviewEditFlyout.Hide();
        UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.AuthorCheck", async () =>
        {
            string? prefill = null;
            try
            {
                var content = Clipboard.GetContent();
                if (content.Contains(StandardDataFormats.Text))
                {
                    var text = (await content.GetTextAsync()).Trim();
                    if (ViewModel.TryParseAuthorUrl(text) is not null) prefill = text;
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                // Nothing to prefill; the address can be typed.
            }
            await ShowAuthorCheckAsync(prefill, autoStart: false);
        });
    }

    private async Task ShowAuthorCheckAsync(string? initialUrl, bool autoStart)
    {
        if (_authorCheckOpen) return;
        _authorCheckOpen = true;
        try
        {
            var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            var urlBox = new TextBox
            {
                Text = initialUrl ?? string.Empty,
                PlaceholderText = ResLoader.GetString("Import_AuthorCheck_UrlPlaceholder"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var startButton = new Button { Content = ResLoader.GetString("Import_AuthorCheck_Start") };
            var progressBar = new ProgressBar { Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = secondary };
            var applyButton = new Button
            {
                Content = ResLoader.GetString("Import_AuthorCheck_Apply"),
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                Visibility = Visibility.Collapsed,
            };
            AuthorCheckResult? lastResult = null;
            // Right padding: the scroll bar sits over the last pixels of the list and clipped long names.
            var results = new StackPanel { Spacing = 12, Padding = new Thickness(0, 0, 16, 0) };

            var addressRow = new Grid { ColumnSpacing = 8 };
            addressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addressRow.Children.Add(urlBox);
            Grid.SetColumn(startButton, 1);
            addressRow.Children.Add(startButton);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = ResLoader.GetString("Import_AuthorCheck_Title"),
                CloseButtonText = ResLoader.GetString("Import_AuthorCheck_Close"),
                DefaultButton = ContentDialogButton.Close,
                Content = new StackPanel
                {
                    Spacing = 10,
                    MinWidth = 520,
                    Children =
                    {
                        addressRow,
                        progressBar,
                        status,
                        applyButton,
                        new ScrollViewer { MaxHeight = 360, Content = results },
                    },
                },
            };

            CancellationTokenSource? cts = null;
            var running = false;

            async Task StartAsync()
            {
                if (running) return;
                var author = ViewModel.TryParseAuthorUrl(urlBox.Text);
                if (author is null) { status.Text = ResLoader.GetString("Import_AuthorCheck_NotAuthor"); return; }
                if (!ViewModel.HasReviewCards) { status.Text = ResLoader.GetString("Import_AuthorCheck_NoCards"); return; }

                running = true;
                startButton.IsEnabled = false;
                urlBox.IsEnabled = false;
                results.Children.Clear();
                applyButton.Visibility = Visibility.Collapsed;
                lastResult = null;
                progressBar.Value = 0;
                progressBar.Visibility = Visibility.Visible;
                status.Text = ResLoader.GetString("Import_AuthorCheck_Listing");
                cts = new CancellationTokenSource();

                // Created here, on the UI thread, so each report arrives back on it.
                var progress = new Progress<AuthorCheckProgress>(report =>
                {
                    progressBar.Value = report.Total == 0 ? 0 : (double)report.Done / report.Total;
                    status.Text = string.Format(ResLoader.GetString("Import_AuthorCheck_Progress"), report.Done, report.Total);
                    if (report.Entry is { } entry) results.Children.Add(BuildAuthorCheckEntry(entry, secondary));
                });

                try
                {
                    var result = await ViewModel.CheckAuthorPostsAsync(author, progress, cts.Token);
                    progressBar.Visibility = Visibility.Collapsed;
                    ShowAuthorCheckSummary(result, status, results, secondary);
                    lastResult = result;
                    if (result.Entries.Any(entry => entry.ClaimedCards is { Count: > 0 })) applyButton.Visibility = Visibility.Visible;
                }
                catch (OperationCanceledException)
                {
                    progressBar.Visibility = Visibility.Collapsed;
                    status.Text = ResLoader.GetString("Import_AuthorCheck_Cancelled");
                }
                catch (Exception ex)
                {
                    App.Services.GetRequiredService<IAppLogger>().LogError("Import.AuthorCheck", ex);
                    progressBar.Visibility = Visibility.Collapsed;
                    status.Text = ResLoader.GetString("Import_AuthorCheck_Failed");
                }
                finally
                {
                    running = false;
                    startButton.IsEnabled = true;
                    urlBox.IsEnabled = true;
                }
            }

            applyButton.Click += (_, _) => UiEventGuard.Run(
                App.Services.GetRequiredService<IAppLogger>(), "Import.AuthorCheckApply", async () =>
                {
                    if (lastResult is null || running) return;
                    running = true;
                    applyButton.IsEnabled = false;
                    startButton.IsEnabled = false;
                    status.Text = ResLoader.GetString("Import_AuthorCheck_Applying");
                    try
                    {
                        var (cards, posts) = await ViewModel.ApplyAuthorCheckAsync(lastResult.Entries);
                        status.Text = string.Format(ResLoader.GetString("Import_AuthorCheck_Applied"), cards, posts);
                        applyButton.Visibility = Visibility.Collapsed;
                    }
                    catch (Exception)
                    {
                        status.Text = ResLoader.GetString("Import_AuthorCheck_ApplyFailed");
                        applyButton.IsEnabled = true;
                    }
                    finally
                    {
                        running = false;
                        startButton.IsEnabled = true;
                    }
                });
            startButton.Click += (_, _) => UiEventGuard.Run(
                App.Services.GetRequiredService<IAppLogger>(), "Import.AuthorCheck", StartAsync);
            urlBox.KeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Enter) return;
                e.Handled = true;
                UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.AuthorCheck", StartAsync);
            };
            dialog.Opened += (_, _) =>
            {
                if (autoStart)
                    UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.AuthorCheck", StartAsync);
            };
            // Closing the dialog is how the run is stopped.
            dialog.Closing += (_, _) => cts?.Cancel();

            await dialog.ShowAsync();
        }
        finally
        {
            _authorCheckOpen = false;
        }
    }

    // "Missing: a.png, b.png, ...and N more" with each name that has an address a link to it:
    // one click opens the file, or its folder with the file selected, in the browser.
    private static void FillLinkedNames(
        TextBlock block, string formatKey, IReadOnlyList<ListedFile> files, Brush brush)
    {
        block.Inlines.Clear();
        if (files.Count == 0) return;

        // The format is "label: {0}"; the names go where {0} is.
        var parts = ResLoader.GetString(formatKey).Split("{0}", 2);
        block.Inlines.Add(new Run { Text = parts[0] });
        var shown = Math.Min(files.Count, ImportViewModel.MissingNamesShown);
        for (var i = 0; i < shown; i++)
        {
            if (i > 0) block.Inlines.Add(new Run { Text = ", " });
            var file = files[i];
            if (Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var link = new Hyperlink { NavigateUri = uri, Foreground = brush };
                link.Inlines.Add(new Run { Text = file.Name });
                block.Inlines.Add(link);
            }
            else
                block.Inlines.Add(new Run { Text = file.Name });
        }
        if (files.Count > shown)
            block.Inlines.Add(new Run
            {
                Text = string.Format(ResLoader.GetString("Import_LinkMatch_MissingMore"), files.Count - shown),
            });
        if (parts.Length > 1 && parts[1].Length > 0) block.Inlines.Add(new Run { Text = parts[1] });
        block.Foreground = brush;
    }

    // "Not started: EP.2 (239 files), EP.3 (146 files)" with each folder that has an address a link
    // to it. These are episodes of the shared folder nothing was taken from, not missing files.
    private static void FillUnstartedFolders(TextBlock block, IReadOnlyList<UnstartedFolder> folders, Brush brush)
    {
        block.Inlines.Clear();
        if (folders.Count == 0) return;

        var parts = ResLoader.GetString("Import_LinkMatch_Unstarted").Split("{0}", 2);
        block.Inlines.Add(new Run { Text = parts[0] });
        // A big archive has dozens of folders; the biggest are the ones worth naming.
        const int shownFolders = 6;
        var ordered = folders.OrderByDescending(folder => folder.Files).ToList();
        var shown = Math.Min(ordered.Count, shownFolders);
        for (var i = 0; i < shown; i++)
        {
            if (i > 0) block.Inlines.Add(new Run { Text = ", " });
            var folder = ordered[i];
            var label = folder.Folder ?? ResLoader.GetString("Import_LinkMatch_RootFolder");
            if (Uri.TryCreate(folder.FolderUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var link = new Hyperlink { NavigateUri = uri };
                link.Inlines.Add(new Run { Text = label });
                block.Inlines.Add(link);
            }
            else
                block.Inlines.Add(new Run { Text = label });
            block.Inlines.Add(new Run { Text = string.Format(ResLoader.GetString("Import_LinkMatch_UnstartedCount"), folder.Files) });
        }
        if (ordered.Count > shown)
            block.Inlines.Add(new Run
            {
                Text = string.Format(ResLoader.GetString("Import_LinkMatch_MissingMore"), ordered.Count - shown),
            });
        if (parts.Length > 1 && parts[1].Length > 0) block.Inlines.Add(new Run { Text = parts[1] });
        block.Foreground = brush;
    }

    private static UIElement BuildAuthorCheckEntry(AuthorPostCheckEntry entry, Brush secondary)
    {
        var match = entry.Match;
        var title = string.IsNullOrWhiteSpace(entry.Title) ? string.Empty : $" {entry.Title}";
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(ResLoader.GetString("Import_AuthorCheck_PostHeader"),
                entry.Post.Id, title, entry.ClaimedCards?.Count ?? match.Matched.Count, match.ListedCount),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        // A post that links a whole archive lists every card of every work in it. Its "missing"
        // files are other works', so once the cards are assigned it gets a single line.
        if (entry.IsCollection)
        {
            panel.Children.Add(new TextBlock
            {
                Text = string.Format(ResLoader.GetString("Import_AuthorCheck_Collection"), match.Matched.Count),
                Foreground = secondary,
                TextWrapping = TextWrapping.Wrap,
            });
            return panel;
        }
        if (entry.ClaimedCards is { } claimedCards && claimedCards.Count < match.Matched.Count)
            panel.Children.Add(new TextBlock
            {
                Text = string.Format(ResLoader.GetString("Import_AuthorCheck_SharedNote"), match.Matched.Count - claimedCards.Count),
                Foreground = secondary,
                TextWrapping = TextWrapping.Wrap,
            });

        if (match.MissingFiles.Count > 0)
        {
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            FillLinkedNames(block, "Import_LinkMatch_Missing", match.MissingFiles, CautionBrush);
            panel.Children.Add(block);
        }
        if (match.InLibraryFiles.Count > 0)
        {
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            FillLinkedNames(block, "Import_LinkMatch_InLibrary", match.InLibraryFiles, OrangeBrush);
            panel.Children.Add(block);
        }
        if (match.UnstartedFolders.Count > 0)
        {
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap };
            FillUnstartedFolders(block, match.UnstartedFolders, secondary);
            panel.Children.Add(block);
        }
        if (match.Missing.Count == 0 && match.InLibrary.Count == 0)
            panel.Children.Add(new TextBlock
            {
                Text = ResLoader.GetString("Import_AuthorCheck_PostComplete"),
                Foreground = secondary,
            });
        return panel;
    }

    private static void ShowAuthorCheckSummary(AuthorCheckResult result, TextBlock status, StackPanel results, Brush secondary)
    {
        if (result.Status == AuthorCheckStatus.ListFailed)
        {
            status.Text = ResLoader.GetString("Import_AuthorCheck_ListFailed");
            return;
        }

        // The entries shown while the check ran did not yet know which cards belong to which post.
        results.Children.Clear();
        foreach (var entry in result.Entries)
            results.Children.Add(BuildAuthorCheckEntry(entry, secondary));

        var owners = result.Entries.Where(entry => !entry.IsCollection).ToList();
        var incomplete = owners.Count(entry => entry.Match.Missing.Count > 0);
        status.Text = string.Format(ResLoader.GetString("Import_AuthorCheck_Summary"),
            result.Checked, owners.Count, incomplete, result.Unreadable, result.WithoutLinks);
        if (result.StoppedEarly)
            status.Text += " " + ResLoader.GetString("Import_AuthorCheck_StoppedEarly");

        if (result.UnclaimedCards.Count > 0)
            results.Children.Add(new TextBlock
            {
                Text = string.Format(ResLoader.GetString("Import_AuthorCheck_Unclaimed"),
                    result.UnclaimedCards.Count, ImportViewModel.NamesWithMore(result.UnclaimedCards)),
                Foreground = secondary,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });
    }
}

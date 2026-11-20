using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SceneGallery.PluginSdk;
using Windows.ApplicationModel.DataTransfer;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// Gives one library card its post or author afterwards, moving it where an
/// import with that source would have put it.
/// </summary>
/// <remarks>
/// Built in code, like the other small dialogs. It offers exactly one action:
/// a collision or a copy already at the destination is reported and stops
/// there. There is no overwrite, rename or link choice, because each of those
/// is a way to lose a card that this dialog would be taking on the user's
/// behalf. Every line it shows comes from <see cref="LibraryCardReidentifier"/>'s
/// plan, so what it says is where the card goes is where it goes.
/// </remarks>
internal static class ReidentifyDialog
{
    private sealed record KnownAuthor(string Name, string Id, string ProviderId)
    {
        public override string ToString() => $"{Name} ({ProviderId}, {Id})";
    }

    private static readonly ContentRating[] Ratings = [ContentRating.AllAges, ContentRating.R18, ContentRating.R18G];

    private static string RatingText(ContentRating rating) => rating switch
    {
        ContentRating.R18 => RatingLabels.R18,
        ContentRating.R18G => RatingLabels.R18G,
        _ => RatingLabels.General,
    };

    /// <summary>Shows the dialog for one card; returns true when the card was moved.</summary>
    public static async Task<bool> ShowAsync(XamlRoot xamlRoot, string filePath)
    {
        var importService = App.Services.GetService<ImportService>();
        var reidentifier = App.Services.GetService<LibraryCardReidentifier>();
        if (importService is null || reidentifier is null) return false;
        var settings = App.Services.GetRequiredService<SettingsViewModel>();
        var logger = App.Services.GetRequiredService<IAppLogger>();

        ArtworkId? artwork = null;
        KnownAuthor? author = null;
        ContentRating? ratingChosen = null;
        ReidentifyPlan? plan = null;
        CancellationTokenSource? planning = null;
        var moved = false;
        var settingRating = false;

        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        var urlBox = new TextBox
        {
            Header = UiText.Get("Reidentify_UrlHeader"),
            PlaceholderText = UiText.Get("Reidentify_UrlPlaceholder"),
        };
        AutomationProperties.SetAutomationId(urlBox, "ReidentifyUrl");
        var fetch = new Button { Content = UiText.Get("Reidentify_Fetch"), VerticalAlignment = VerticalAlignment.Bottom };
        AutomationProperties.SetAutomationId(fetch, "ReidentifyFetch");
        var urlRow = new Grid { ColumnSpacing = 8 };
        urlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        urlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        urlRow.Children.Add(urlBox);
        Grid.SetColumn(fetch, 1);
        urlRow.Children.Add(fetch);

        var postLine = new TextBlock { Foreground = secondary, TextWrapping = TextWrapping.Wrap };

        var authorBox = new AutoSuggestBox { Header = UiText.Get("Reidentify_AuthorHeader") };
        AutomationProperties.SetAutomationId(authorBox, "ReidentifyAuthor");

        var searchByImage = new Button { Content = UiText.Get("Reidentify_SearchByImage") };
        AutomationProperties.SetAutomationId(searchByImage, "ReidentifySearchByImage");
        // Only with a key: without one the search cannot run, and a button
        // that always fails teaches the user to ignore it.
        searchByImage.Visibility = string.IsNullOrWhiteSpace(settings.SauceNaoApiKey)
            ? Visibility.Collapsed : Visibility.Visible;

        var rating = new ComboBox { Header = UiText.Get("Reidentify_RatingHeader"), MinWidth = 120 };
        foreach (var value in Ratings) rating.Items.Add(RatingText(value));
        rating.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(rating, "ReidentifyRating");

        var destinationLine = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        AutomationProperties.SetAutomationId(destinationLine, "ReidentifyDestination");
        var outcomeBar = new InfoBar { IsClosable = false, IsOpen = false };
        AutomationProperties.SetAutomationId(outcomeBar, "ReidentifyOutcome");
        var busy = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };

        var panel = new StackPanel { Spacing = 12, MinWidth = 420, MaxWidth = 560 };
        panel.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(filePath),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        panel.Children.Add(urlRow);
        panel.Children.Add(postLine);
        panel.Children.Add(authorBox);
        panel.Children.Add(searchByImage);
        panel.Children.Add(rating);
        panel.Children.Add(busy);
        panel.Children.Add(destinationLine);
        panel.Children.Add(outcomeBar);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = UiText.Get("Reidentify_Title"),
            Content = new ScrollViewer { Content = panel },
            PrimaryButtonText = UiText.Get("Reidentify_Confirm"),
            CloseButtonText = UiText.Get("Reidentify_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };

        void ShowOutcome(InfoBarSeverity severity, string message)
        {
            outcomeBar.Severity = severity;
            outcomeBar.Message = message;
            outcomeBar.IsOpen = true;
        }

        // Every change of input makes a new plan; an older one still running
        // is cancelled so its answer cannot land after the newer one.
        async Task ReplanAsync()
        {
            planning?.Cancel();
            var cts = planning = new CancellationTokenSource();
            plan = null;
            dialog.IsPrimaryButtonEnabled = false;
            outcomeBar.IsOpen = false;
            if (artwork is null && author is null)
            {
                destinationLine.Text = UiText.Get("Reidentify_NeedPostOrAuthor");
                return;
            }

            busy.Visibility = Visibility.Visible;
            try
            {
                var request = new ReidentifyRequest(filePath, artwork,
                    author?.Name, author?.Id,
                    string.IsNullOrEmpty(author?.ProviderId) ? null : author.ProviderId, ratingChosen);
                var next = await reidentifier.PlanAsync(request, cts.Token);
                if (cts.IsCancellationRequested) return;
                plan = next;
                if (next.Fetched is { } info)
                {
                    postLine.Text = string.IsNullOrEmpty(info.Title) ? info.AuthorName : $"{info.Title} / {info.AuthorName}";
                    // The post's rating is the default until the user picks one.
                    if (ratingChosen is null)
                    {
                        settingRating = true;
                        rating.SelectedIndex = Array.IndexOf(Ratings, info.Rating) is >= 0 and var i ? i : 0;
                        settingRating = false;
                    }
                }
                else
                {
                    postLine.Text = "";
                }
                destinationLine.Text = next.Problem is { } problem
                    ? ProblemText(problem)
                    : UiText.Format("Reidentify_Destination", RelativeToRoot(next.DestinationPath, settings));
                dialog.IsPrimaryButtonEnabled = next.Problem is null;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError("Reidentify.Plan", ex, filePath);
                destinationLine.Text = UiText.Format("Reidentify_Failed", ex.Message);
            }
            finally
            {
                if (ReferenceEquals(planning, cts)) busy.Visibility = Visibility.Collapsed;
            }
        }

        void Replan() => _ = ReplanAsync();

        void TakeUrl()
        {
            var text = urlBox.Text.Trim();
            // Takes a post address or a bare id, as the import page's box does.
            // Not CreateManualArtworkId: it guesses the first provider for any
            // text, and a guessed post would move the card on a guess.
            artwork = string.IsNullOrEmpty(text) ? null : importService.ResolveReviewArtworkInput(text, null);
            if (artwork is not null) author = null;
            if (!string.IsNullOrEmpty(text) && artwork is null)
            {
                postLine.Text = UiText.Get("Reidentify_NotFound");
                return;
            }
            Replan();
        }

        fetch.Click += (_, _) => TakeUrl();
        urlBox.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            TakeUrl();
        };

        rating.SelectionChanged += (_, _) =>
        {
            if (settingRating || rating.SelectedIndex < 0) return;
            ratingChosen = Ratings[rating.SelectedIndex];
            Replan();
        };

        // Known authors are loaded once; the box filters them as the user types.
        IReadOnlyList<KnownAuthor> authors = [];
        authorBox.TextChanged += (box, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var query = box.Text.Trim();
            box.ItemsSource = query.Length == 0
                ? null
                : authors.Where(a => a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                     || a.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(30).ToList();
        };
        authorBox.SuggestionChosen += (box, e) =>
        {
            if (e.SelectedItem is not KnownAuthor chosen) return;
            author = chosen;
            box.Text = chosen.Name;
            // An author alone is the fallback for an unknown post: choosing
            // one clears the post so the plan says what will really happen.
            artwork = null;
            urlBox.Text = "";
            postLine.Text = "";
            Replan();
        };

        searchByImage.Click += async (_, _) =>
        {
            searchByImage.IsEnabled = false;
            busy.Visibility = Visibility.Visible;
            try
            {
                var result = await importService.SearchReverseImageAsync(filePath, settings.SauceNaoApiKey, CancellationToken.None);
                if (result?.ArtworkId is { } found)
                {
                    artwork = found;
                    author = null;
                    urlBox.Text = found.Id;
                    Replan();
                }
                else if (result is { AuthorId: { } id, AuthorName: { } name })
                {
                    author = new KnownAuthor(name, id, result.ArtworkId?.ProviderId ?? "");
                    authorBox.Text = name;
                    Replan();
                }
                else
                {
                    postLine.Text = UiText.Get("Reidentify_NotFound");
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Reidentify.SearchByImage", ex, filePath);
                postLine.Text = UiText.Format("Reidentify_Failed", ex.Message);
            }
            finally
            {
                searchByImage.IsEnabled = true;
                busy.Visibility = Visibility.Collapsed;
            }
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            // The dialog stays open unless the card moved: every other outcome
            // is explained in place and leaves the user to decide.
            args.Cancel = true;
            if (plan is not { Problem: null } ready) return;
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            busy.Visibility = Visibility.Visible;
            try
            {
                var result = await reidentifier.ApplyAsync(ready, CancellationToken.None);
                switch (result.Outcome)
                {
                    case LibraryCardMoveOutcome.Moved:
                        moved = true;
                        if (result.Error is null)
                        {
                            AppToast.Show(UiText.Get("Reidentify_Done"), InfoBarSeverity.Success, key: "reidentify");
                        }
                        else
                        {
                            AppToast.Show(UiText.Get("Reidentify_DoneNoSidecar"), InfoBarSeverity.Warning, key: "reidentify");
                        }
                        args.Cancel = false;
                        break;
                    case LibraryCardMoveOutcome.AlreadyInPlace:
                        ShowOutcome(InfoBarSeverity.Informational, UiText.Get("Reidentify_AlreadyInPlace"));
                        break;
                    case LibraryCardMoveOutcome.Collision:
                        ShowOutcome(InfoBarSeverity.Error, UiText.Get("Reidentify_Collision"));
                        break;
                    case LibraryCardMoveOutcome.DuplicateAtDestination:
                        ShowOutcome(InfoBarSeverity.Warning, UiText.Get("Reidentify_Duplicate"));
                        break;
                    case LibraryCardMoveOutcome.SourceMissing:
                        ShowOutcome(InfoBarSeverity.Error, UiText.Get("Reidentify_SourceMissing"));
                        break;
                    default:
                        ShowOutcome(InfoBarSeverity.Error, UiText.Format("Reidentify_Failed", result.Error ?? ""));
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Reidentify.Apply", ex, filePath);
                ShowOutcome(InfoBarSeverity.Error, UiText.Format("Reidentify_Failed", ex.Message));
            }
            finally
            {
                busy.Visibility = Visibility.Collapsed;
                deferral.Complete();
            }
        };

        dialog.Opened += async (_, _) =>
        {
            destinationLine.Text = UiText.Get("Reidentify_NeedPostOrAuthor");
            try
            {
                authors = [.. (await importService.GetKnownAuthorsAsync(CancellationToken.None))
                    .Select(a => new KnownAuthor(a.Name, a.Id, a.ProviderId))
                    .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
            }
            catch (Exception ex)
            {
                logger.LogError("Reidentify.KnownAuthors", ex);
            }

            // As the import page does: a post address the user just copied is
            // what they came here to give, so it is filled in for them.
            if (await ReadClipboardUrlAsync() is { } url && importService.ResolveReviewArtworkInput(url, null) is not null)
            {
                urlBox.Text = url;
                TakeUrl();
            }
        };

        await dialog.ShowAsync();
        planning?.Cancel();
        return moved;
    }

    private static async Task<string?> ReadClipboardUrlAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return null;
            var text = (await content.GetTextAsync())?.Trim();
            return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? text : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // The clipboard is locked or not readable yet; the box stays empty.
            return null;
        }
    }

    private static string ProblemText(ReidentifyProblem problem) => problem switch
    {
        ReidentifyProblem.NotInLibrary => UiText.Get("Reidentify_NotInLibrary"),
        ReidentifyProblem.PostNotFound => UiText.Get("Reidentify_NotFound"),
        _ => UiText.Get("Reidentify_NeedPostOrAuthor"),
    };

    /// <summary>The destination as the user knows it: below the library root it is in.</summary>
    private static string RelativeToRoot(string path, SettingsViewModel settings)
    {
        var root = settings.FolderPaths.Concat(settings.CharacterFolderPaths).Concat(settings.CoordinateFolderPaths)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)))
            .Where(r => path.StartsWith(r + Path.DirectorySeparatorChar, PathComparison.Comparison))
            .MaxBy(r => r.Length);
        return root is null ? path : Path.Combine(Path.GetFileName(root), Path.GetRelativePath(root, path));
    }
}

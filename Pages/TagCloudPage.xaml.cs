using KoikatsuSceneGallery.Controls;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SceneGallery.PluginSdk;
using Windows.Foundation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class TagCloudPage : Page
{
    private readonly TagCloudService _tags = App.Services.GetRequiredService<TagCloudService>();
    private readonly TagDictionaryService _dictionary = App.Services.GetRequiredService<TagDictionaryService>();
    private readonly SettingsViewModel _settings = App.Services.GetRequiredService<SettingsViewModel>();
    private readonly IAppLogger _logger = App.Services.GetRequiredService<IAppLogger>();
    private readonly TagCloudMotion _motion = new();

    private IReadOnlyList<TagCloudProviderGroup> _cloud = [];
    private bool _syncing;
    private bool _loaded;
    private bool _active;
    private bool _transitioning;
    private bool _showingResults;
    private bool _layoutPending;
    /// <summary>
    /// Something finished while the page was off screen and must be built when
    /// it comes back. Touching the cloud's elements while the page is out of
    /// the visual tree is what this exists to avoid.
    /// </summary>
    private bool _rebuildPending;
    /// <summary>
    /// The sidecars were read again (a re-fetch wrote more) since this page
    /// built its cloud. Applied at once when the page is next entered; while it
    /// is on screen it is only offered, because re-placing the cloud moves every
    /// word the reader was looking at.
    /// </summary>
    private bool _cloudStale;
    private int _layoutGeneration;
    private int _transitionGeneration;
    private Size _layoutSize;
    private string? _renderedProviderId;

    /// <summary>
    /// Platform dictionary answers, keyed by raw tag. Only ever read from the
    /// plugin's own cache when the cloud is built, so a cold start renders at
    /// once; the background pass below fills it and re-places the cloud in one
    /// go rather than nudging words about as each answer lands.
    /// </summary>
    private readonly Dictionary<string, TagArticle> _articles = new(StringComparer.Ordinal);
    private CancellationTokenSource? _warming;

    // Kept so going back can reverse the exact words that flew away, instead of
    // rebuilding the cloud and replaying its arrival.
    private Button? _chosenTag;
    private Point _revealCenter;

    public TagCloudPage()
    {
        InitializeComponent();
        // Cached so returning from a card's detail page does not rebuild the
        // cloud; the idle animation is stopped explicitly on the way out.
        NavigationCacheMode = NavigationCacheMode.Required;
        _settings.BrowseOriginChanged += OnBrowseOriginChanged;
        _tags.CloudChanged += OnCloudChanged;
    }

    private void OnCloudChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        // The first build raises this too, and this page asked for it.
        if (!_loaded || _tags.Cloud is null || ReferenceEquals(_tags.Cloud, _cloud)) return;
        _cloudStale = true;
        UpdateStaleBar();
    });

    private void UpdateStaleBar()
        => CloudStaleBar.IsOpen = _cloudStale && _active && !_showingResults && _cloud.Count > 0;

    private void StaleRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_active || _transitioning || _showingResults) return;
        ApplyNewCloud(TagCloudMotion.CloudEntryPace.Swap);
    }

    private void ApplyNewCloud(TagCloudMotion.CloudEntryPace pace)
    {
        _cloudStale = false;
        _cloud = _tags.Cloud ?? _cloud;
        UpdateStaleBar();
        _transitionGeneration++;
        _layoutGeneration++;
        _motion.StopIdle();
        ShowCloud();
        BuildProviderBar(pace);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        if (!_loaded)
        {
            _loaded = true;
            Load();
        }
        else if (_cloudStale && !_showingResults)
        {
            // Nobody was looking at the old cloud, so there is nothing to
            // disturb: the new one simply arrives.
            _rebuildPending = false;
            ApplyNewCloud(TagCloudMotion.CloudEntryPace.Arrival);
        }
        else if (_rebuildPending)
        {
            // Something finished while the page was away and could not be shown
            // then. Now it can.
            _rebuildPending = false;
            ShowCloud();
            BuildProviderBar(TagCloudMotion.CloudEntryPace.Arrival);
        }
        else if (!_showingResults && _cloud.Count > 0)
        {
            ShowCloud();
            if (ProviderBar.SelectedItem?.Tag is string providerId && providerId != _renderedProviderId)
                BuildCloud(_cloud.FirstOrDefault(group => group.ProviderId == providerId),
                    TagCloudMotion.CloudEntryPace.Swap);
            else LayoutCloud(entry: null);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _active = false;
        _layoutGeneration++;
        _transitionGeneration++;
        SetTransitioning(false);
        // Keep the revealed endpoints when returning from a card's details.
        // An interrupted cloud transition instead resumes as a settled cloud.
        _motion.StopIdle(resetVisuals: !_showingResults);
        // The lookups are paced seconds apart; letting a pass run on for a page
        // nobody is looking at would keep pixiv busy for nothing.
        _warming?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private void Load() => UiEventGuard.Run(_logger, "TagCloud.Load", async () =>
    {
        BusyPanel.Visibility = Visibility.Visible;
        BusyRing.IsActive = true;
        CloudHost.Visibility = Visibility.Collapsed;
        try
        {
            _cloud = _tags.Cloud ?? await _tags.BuildAsync();
        }
        finally
        {
            BusyRing.IsActive = false;
            BusyPanel.Visibility = Visibility.Collapsed;
        }

        // The scan reads every sidecar on disk, so this await lasts seconds and
        // the page can be left during it. Building then puts sixty buttons into
        // a panel that is no longer in the visual tree and starts composition
        // animations and a scoped batch against their visuals — which is a
        // native fault, not an exception anything here could catch.
        if (!_active) { _rebuildPending = true; return; }
        BuildProviderBar(TagCloudMotion.CloudEntryPace.Arrival);
    });

    /// <summary>
    /// One entry per platform that actually has tags. The aggregator emits no
    /// group for a platform with none, so "hide the tag UI for platforms
    /// without tags" needs no extra check here.
    /// </summary>
    private void BuildProviderBar(TagCloudMotion.CloudEntryPace pace)
    {
        // Only while the bar is on screen. This page is cached, so a return to
        // it rebuilds before the bar is back in the tree; replacing the items
        // then leaves the selection naming one no longer in the list, and the
        // next measure fails natively — the Authors page crashed exactly so.
        // Waiting for Loaded costs one frame.
        if (!ProviderBar.IsLoaded)
        {
            _pendingBarPace = pace;
            ProviderBar.Loaded -= ProviderBar_BuildPending;
            ProviderBar.Loaded += ProviderBar_BuildPending;
            return;
        }

        // The title bar already names a platform unless it says "all", and two
        // controls choosing the same thing disagree the moment one of them is
        // touched — which is exactly what this page showed: BepisDB selected
        // above, pixiv's tags below. So this bar exists only for "all", where
        // the title bar names no platform for it to contradict.
        var selected = _settings.EffectiveBrowseOrigin;
        var groups = selected.IsAll
            ? _cloud
            : [.. _cloud.Where(group =>
                string.Equals(group.ProviderId, selected.ProviderId, StringComparison.OrdinalIgnoreCase))];

        _syncing = true;
        ProviderBar.SelectedItem = null;
        ProviderBar.Items.Clear();
        foreach (var group in groups)
        {
            var item = new SelectorBarItem { Text = ProviderName(group.ProviderId), Tag = group.ProviderId };
            // Every other control in the app carries one, and without it this
            // item's automation identity changes with the layout.
            AutomationProperties.SetAutomationId(item, $"TagProvider_{group.ProviderId}");
            ProviderBar.Items.Add(item);
        }

        var empty = groups.Count == 0;
        if (empty) ShowEmpty("TagCloud_EmptyTitle.Text", "TagCloud_EmptyDescription.Text");
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ProviderBar.Visibility = !empty && selected.IsAll ? Visibility.Visible : Visibility.Collapsed;
        CloudHost.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        // Inside the guard, and the cloud built explicitly afterwards. Selecting
        // an item outside it runs Provider_Changed, whose ShowCloud puts this
        // bar back on screen — which is how a bar that had just been hidden
        // reappeared. Relying on that side effect to build the first cloud is
        // what made the ordering matter in the first place.
        // The platform being read stays chosen across a rebuild; a new cloud
        // arriving is no reason to jump back to the first one.
        var keep = Math.Max(0, groups.ToList().FindIndex(group => group.ProviderId == _renderedProviderId));
        if (!empty) ProviderBar.SelectedItem = ProviderBar.Items[keep];
        _syncing = false;

        if (empty) { CloudPanel.Children.Clear(); return; }
        BuildCloud(groups[keep], pace);
    }

    private TagCloudMotion.CloudEntryPace _pendingBarPace;

    private void ProviderBar_BuildPending(object sender, RoutedEventArgs e)
    {
        ProviderBar.Loaded -= ProviderBar_BuildPending;
        if (_active) BuildProviderBar(_pendingBarPace);
    }

    /// <summary>
    /// Rebuilds for the platform the title bar now names.
    /// </summary>
    private void OnBrowseOriginChanged(CardOriginSelection origin)
    {
        _ = origin;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_loaded) return;
            if (!_active) { _rebuildPending = true; return; }
            // A platform switch is a different question, not a step back inside
            // the current one, so any open result set is abandoned.
            _transitionGeneration++;
            _layoutGeneration++;
            SetTransitioning(false);
            _motion.StopIdle();
            ShowCloud();
            BuildProviderBar(TagCloudMotion.CloudEntryPace.Swap);
        });
    }

    private static string ProviderName(string providerId) => PlatformDisplayName.For(providerId);

    private void Provider_Changed(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_syncing || _transitioning || sender.SelectedItem?.Tag is not string providerId) return;

        var generation = ++_transitionGeneration;
        _layoutGeneration++;

        var next = _cloud.FirstOrDefault(group => group.ProviderId == providerId);
        var current = TagElements().ToArray();
        ShowCloud();

        // First arrival earns the unhurried sequence. Switching platforms does
        // not: replaying it there is just a wait, and cutting the old cloud
        // dead before it makes the switch feel broken as well as slow.
        if (current.Length == 0)
        {
            BuildCloud(next, TagCloudMotion.CloudEntryPace.Arrival);
            return;
        }

        SetTransitioning(true);
        _motion.SwapOut(current, () =>
        {
            if (!_active || generation != _transitionGeneration) return;
            SetTransitioning(false);
            BuildCloud(next, TagCloudMotion.CloudEntryPace.Swap);
        });
    }

    private void BuildCloud(TagCloudProviderGroup? group, TagCloudMotion.CloudEntryPace pace)
    {
        _motion.StopIdle();
        _renderedProviderId = group?.ProviderId;
        CloudPanel.Children.Clear();
        if (group is null || group.Tags.Count == 0) return;

        LoadCachedArticles(group);
        group = FoldAliases(group);

        var min = group.Tags.Min(tag => tag.Count);
        var max = group.Tags.Max(tag => tag.Count);

        // Largest first, which is the order the spiral placement wants: the big
        // words take the middle and the small ones fill the gaps around them.
        foreach (var tag in group.Tags)
        {
            var size = TagCloudAggregator.FontSize(tag.Count, min, max);
            var button = new Button
            {
                Style = Application.Current.Resources["SubtleButtonStyle"] as Style,
                Background = null,
                // Tight and ragged: the row height follows the tallest tag on
                // it, and WrapPanel centres the shorter ones against it.
                Padding = new Thickness(6, 2, 6, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = tag,
                // On the button, not just the text: the drift reads FontSize off
                // the element it animates, and the text inherits it anyway.
                FontSize = size,
                FontWeight = size >= (TagCloudAggregator.MinFontSize + TagCloudAggregator.MaxFontSize) / 2
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal,
                Content = new TextBlock { Text = LabelFor(tag) },
            };
            AutomationProperties.SetName(button, UiText.Format("TagCloud_TagAccessibleName", LabelFor(tag), tag.Count));
            ToolTipService.SetToolTip(button, TooltipFor(tag));
            button.Click += Tag_Click;
            CloudPanel.Children.Add(button);
        }

        // Layout has to settle before the words can be measured and placed.
        var generation = ++_layoutGeneration;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_active && generation == _layoutGeneration && !_transitioning)
                LayoutCloud(pace);
        });

        StartWarming(group);
    }

    /// <summary>
    /// Merges the platform's alias tags into the tags they name.
    /// </summary>
    /// <remarks>
    /// Only the provider decides what is an alias; nothing here inspects a tag.
    /// Before the dictionary has been warmed there are no answers yet, so the
    /// first view shows the spellings separately and the fold arrives with the
    /// same pass that brings the translations.
    /// </remarks>
    private TagCloudProviderGroup FoldAliases(TagCloudProviderGroup group)
    {
        if (!_dictionary.IsAvailableFor(group.ProviderId)) return group;
        return TagAliasFold.Fold([group], name =>
            _articles.TryGetValue(name, out var article) ? article.AliasOf : null)[0];
    }

    /// <summary>
    /// Fills <see cref="_articles"/> from whatever the plugin already holds.
    /// Cache-only and synchronous by design: the cloud must appear now.
    /// </summary>
    private void LoadCachedArticles(TagCloudProviderGroup group)
    {
        if (!_dictionary.IsAvailableFor(group.ProviderId)) return;
        foreach (var tag in group.Tags)
        {
            if (_articles.ContainsKey(tag.Name)) continue;
            if (_dictionary.Cached(group.ProviderId, tag.Name) is { } article)
                _articles[tag.Name] = article;
        }
    }

    /// <summary>
    /// Looks up whatever was not cached, then re-places the cloud once if the
    /// labels changed.
    /// </summary>
    /// <remarks>
    /// Deliberately one re-place at the end rather than relabelling each word
    /// as its answer lands. A word's box is sized by its text, and the spiral
    /// packs those boxes against each other — so changing one word's text in
    /// place makes it overlap its neighbours. Re-running the placement is the
    /// only honest way to keep the shape, and doing it per word would have the
    /// whole cloud rearranging itself for a minute.
    ///
    /// The rebuild calls back into here, but by then every tag is in
    /// <see cref="_articles"/>, so the second pass reports nothing new and it
    /// stops.
    /// </remarks>
    private void StartWarming(TagCloudProviderGroup group)
    {
        _warming?.Cancel();
        _warming?.Dispose();
        _warming = null;
        if (!_dictionary.IsAvailableFor(group.ProviderId)) return;

        var pending = group.Tags.Select(tag => tag.Name).Where(name => !_articles.ContainsKey(name)).ToArray();
        if (pending.Length == 0) return;

        var cts = new CancellationTokenSource();
        _warming = cts;
        var providerId = group.ProviderId;

        UiEventGuard.Run(_logger, "TagCloud.WarmDictionary", async () =>
        {
            var changed = false;
            try
            {
                await _dictionary.WarmAsync(providerId, pending, (name, article) =>
                {
                    _articles[name] = article;
                    // An alias changes the cloud as much as a label does: most
                    // of them (koikatsu, koikatsu!) carry no translation at all,
                    // so testing the label alone left their fold unapplied.
                    if (!string.IsNullOrWhiteSpace(article.Translation)
                        || !string.IsNullOrWhiteSpace(article.AliasOf)) changed = true;
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!changed || cts.IsCancellationRequested) return;
            // Only worth disturbing the page while its own cloud is the thing
            // on screen.
            if (!_active || _showingResults || _transitioning || _renderedProviderId != providerId) return;

            BuildCloud(_cloud.FirstOrDefault(candidate => candidate.ProviderId == providerId),
                TagCloudMotion.CloudEntryPace.Swap);
        });
    }

    /// <summary>
    /// Measures every word and drops it into the cloud. Words that find no room
    /// are hidden rather than left stacked at the origin.
    /// </summary>
    private void LayoutCloud(TagCloudMotion.CloudEntryPace? entry)
    {
        if (!_active || _transitioning) return;
        var width = CloudHost.ActualWidth;
        var height = CloudHost.ActualHeight;
        var words = CloudPanel.Children.OfType<Control>().ToArray();
        if (width <= 0 || height <= 0 || words.Length == 0) return;

        _layoutPending = false;
        _layoutSize = new Size(width, height);
        var generation = ++_layoutGeneration;

        CloudPanel.Width = width;
        CloudPanel.Height = height;

        var sizes = new (double Width, double Height)[words.Length];
        for (var i = 0; i < words.Length; i++)
        {
            words[i].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            sizes[i] = (words[i].DesiredSize.Width, words[i].DesiredSize.Height);
        }

        var places = WordCloudLayout.Place(sizes, width, height);
        var shown = new List<Control>(words.Length);
        for (var i = 0; i < words.Length; i++)
        {
            if (places[i] is not { } rect)
            {
                words[i].Visibility = Visibility.Collapsed;
                continue;
            }
            words[i].Visibility = Visibility.Visible;
            Canvas.SetLeft(words[i], rect.X);
            Canvas.SetTop(words[i], rect.Y);
            shown.Add(words[i]);
        }

        _motion.StopIdle();
        // One more hop: the words were measured above but the Canvas arranges
        // them on the next pass, and an animation started before that has no
        // real size to centre itself on.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || _transitioning || generation != _layoutGeneration
                || CloudHost.Visibility != Visibility.Visible) return;
            if (entry is { } pace) _motion.EnterFromDepth(shown, () => _motion.StartIdle(shown), pace);
            else _motion.StartIdle(shown);
        });
    }

    /// <summary>
    /// Re-places the cloud when the window changes shape. Cheap enough to do
    /// inline, and without it the blob keeps the old window's proportions.
    /// </summary>
    private void Cloud_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_active || CloudHost.Visibility != Visibility.Visible || e.NewSize == _layoutSize) return;

        // Showing the cloud during Back schedules a layout pass. It must not
        // replace the return animation with idle loops halfway through it.
        _layoutPending = true;
        if (_transitioning) return;

        // SizeChanged is raised by the layout manager while the pass that
        // produced the new size is still running, and LayoutCloud measures the
        // words directly and writes CloudPanel's own Width and Height. Doing
        // that from here asks the layout engine to answer a question it is in
        // the middle of answering; the native side returns E_FAIL, which XAML
        // stows and later turns into a fail-fast that no managed handler can
        // catch — marking Application.UnhandledException handled does not stop
        // it. Low priority runs after layout and render have both finished, so
        // the same work happens outside the pass.
        var generation = _layoutGeneration;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!_active || _transitioning || !_layoutPending) return;
            if (generation != _layoutGeneration) return;
            if (CloudHost.Visibility != Visibility.Visible) return;
            LayoutCloud(entry: null);
        });
    }

    private IEnumerable<Control> TagElements() =>
        CloudPanel.Children.OfType<Control>().Where(w => w.Visibility == Visibility.Visible);

    /// <summary>
    /// What the word says. The platform dictionary is asked first, because it
    /// answers in the language being read now, while a sidecar's translation
    /// was fixed at import in whatever language the plugin asked for then.
    /// Either only counts when it is really in the reader's language — see
    /// <see cref="TagTranslationPolicy"/>; otherwise the tag itself is shown.
    /// </summary>
    private string LabelFor(TagCloudEntry tag)
        => TagTranslationPolicy.Label(
            _dictionary.Language,
            tag.Name,
            _articles.TryGetValue(tag.Name, out var article) ? article.Translation : null,
            tag.TranslatedName);

    /// <summary>
    /// The count, plus the original tag whenever the word on screen is not it.
    /// A translated cloud is unsearchable otherwise: the user sees "Genshin
    /// Impact" and has no way to learn that the tag is 原神.
    /// </summary>
    private object TooltipFor(TagCloudEntry tag)
    {
        var count = UiText.Format("TagCloud_TagCount", tag.Count);
        var label = LabelFor(tag);
        if (string.Equals(label, tag.Name, StringComparison.Ordinal) && tag.MergedFrom.Count == 0)
            return count;

        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock { Text = tag.Name, TextWrapping = TextWrapping.Wrap });
        // A count that grew by merging has to say so, otherwise the number
        // silently disagrees with what the platform itself reports.
        if (tag.MergedFrom.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = UiText.Format("TagCloud_TagMergedFrom", string.Join("、", tag.MergedFrom)),
                Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
            });
        }
        panel.Children.Add(new TextBlock
        {
            Text = count,
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            Opacity = 0.8,
        });
        return panel;
    }

    private void Tag_Click(object sender, RoutedEventArgs e)
    {
        if (!_active || _transitioning || _showingResults
            || sender is not Button chosen || chosen.Tag is not TagCloudEntry tag) return;

        var generation = ++_transitionGeneration;
        _layoutGeneration++;
        SetTransitioning(true);

        _chosenTag = chosen;
        _revealCenter = CenterIn(chosen, ResultsHost);
        _motion.Reveal(chosen, TagElements().Where(other => !ReferenceEquals(other, chosen)),
            () =>
            {
                if (_active && generation == _transitionGeneration)
                    ShowResults(tag, _revealCenter);
            });
    }

    private void ShowResults(TagCloudEntry tag, Point center)
    {
        var matches = _tags.CardsFor(tag);
        _showingResults = true;
        UpdateStaleBar();
        SetTransitioning(false);
        ResultsHost.IsHitTestVisible = true;
        CloudHost.Visibility = Visibility.Collapsed;
        ProviderBar.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;
        Heading.Text = LabelFor(tag);
        ShowArticle(tag);

        if (matches.Count == 0)
        {
            ResultsContent.Content = null;
            // A tag with no cards is not the same as a library with no tags:
            // the sidecars cover every artwork fetched, not only the ones whose
            // files were actually imported. The article still belongs on screen
            // — being told what the tag means is most of why it was clicked.
            ResultsHost.Visibility = ArticleCard.Visibility;
            ShowEmpty("TagCloud_NoCardsTitle", "TagCloud_NoCardsDescription");
            EmptyPanel.Visibility = Visibility.Visible;
            return;
        }

        EmptyPanel.Visibility = Visibility.Collapsed;
        ResultsContent.Content = matches.Count == 1
            ? CreateBrowser(matches.First().Key, matches.First().Value, LabelFor(tag))
            : CreateBrowserPivot(matches, LabelFor(tag));
        ResultsHost.Visibility = Visibility.Visible;
        TagCloudMotion.PopIn(ResultsHost, center);
    }

    /// <summary>
    /// Shows what the platform says about the tag, fetching it when it was not
    /// already cached.
    /// </summary>
    /// <remarks>
    /// Starts collapsed and appears only once there is something to show, so a
    /// platform without a dictionary plugin — or a tag nobody has written about
    /// — leaves no empty frame behind.
    /// </remarks>
    private void ShowArticle(TagCloudEntry tag)
    {
        var providerId = _renderedProviderId;
        ArticleCard.Visibility = Visibility.Collapsed;
        if (providerId is null || !_dictionary.IsAvailableFor(providerId)) return;

        if (_articles.TryGetValue(tag.Name, out var cached))
        {
            RenderArticle(tag, cached);
            return;
        }

        var generation = _transitionGeneration;
        UiEventGuard.Run(_logger, "TagCloud.FetchArticle", async () =>
        {
            var article = await _dictionary.FetchAsync(providerId, tag.Name, CancellationToken.None);
            if (article is null) return;
            _articles[tag.Name] = article;
            // The user may have gone back, or clicked a different tag, while
            // this was in flight.
            if (_active && _showingResults && generation == _transitionGeneration)
                RenderArticle(tag, article);
        });
    }

    private void RenderArticle(TagCloudEntry tag, TagArticle article)
    {
        _ = tag;
        ArticleSummary.Text = article.Summary ?? string.Empty;
        ArticleSummary.Visibility = article.HasArticle ? Visibility.Visible : Visibility.Collapsed;

        // The innermost parent, shown last and made clickable. For an alias
        // ("「東方」のローマ字表記。") that is the entry actually worth reading,
        // and pixiv marks aliases no other way — so it is offered rather than
        // followed automatically.
        var parent = article.ParentTags.Count > 0 ? article.ParentTags[^1] : null;
        ArticleParentRow.Visibility = parent is null ? Visibility.Collapsed : Visibility.Visible;
        if (parent is not null)
        {
            ArticleParents.Text = UiText.Format(
                "TagCloud_ArticleParents", string.Join(" › ", article.ParentTags.SkipLast(1)));
            ArticleParents.Visibility = article.ParentTags.Count > 1
                ? Visibility.Visible
                : Visibility.Collapsed;
            ArticleParentLink.Content = parent;
            ArticleParentLink.NavigateUri = new Uri(PixivArticleUrl(parent));
        }

        var hasLink = !string.IsNullOrWhiteSpace(article.ArticleUrl);
        ArticleLink.Visibility = hasLink ? Visibility.Visible : Visibility.Collapsed;
        if (hasLink) ArticleLink.NavigateUri = new Uri(article.ArticleUrl!);

        ArticleCard.Visibility = article.HasArticle || parent is not null || hasLink
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (ArticleCard.Visibility == Visibility.Visible) ResultsHost.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The parent's own article address, derived from the one the provider gave
    /// for this tag so the app does not hard-code a platform's URL shape.
    /// </summary>
    private string PixivArticleUrl(string tag)
    {
        var template = _articles.Values.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.ArticleUrl))?.ArticleUrl;
        if (template is null) return "https://dic.pixiv.net/a/" + Uri.EscapeDataString(tag);
        var cut = template.LastIndexOf('/');
        return cut < 0 ? template : template[..(cut + 1)] + Uri.EscapeDataString(tag);
    }

    /// <summary>
    /// A fresh browser per tag rather than re-initializing one: in scoped mode
    /// Initialize returns before the event wiring, so reuse is not obviously
    /// safe, and replacing the control fires the old one's Unloaded for us.
    /// </summary>
    private LibraryBrowser CreateBrowser(
        LibraryKind kind, IReadOnlyList<CardBase> cards, string title, bool showKindHeading = true)
    {
        var browser = new LibraryBrowser { ShowKindHeading = showKindHeading };
        browser.Initialize(kind, cards, title);
        browser.Loaded += (_, _) =>
        {
            browser.Activate(Frame);
            DispatcherQueue.TryEnqueue(() => TagCloudMotion.PopInVisibleContainers(browser.CardGrid));
        };
        browser.Unloaded += (_, _) => browser.Deactivate();
        return browser;
    }

    private Pivot CreateBrowserPivot(IReadOnlyDictionary<LibraryKind, IReadOnlyList<CardBase>> matches, string title)
    {
        var pivot = new Pivot();
        foreach (var (kind, cards) in matches)
            pivot.Items.Add(new PivotItem
            {
                Header = UiText.Get($"TagCloud_Kind_{kind}"),
                // The tab header names the kind; the browser need not repeat it.
                Content = CreateBrowser(kind, cards, title, showKindHeading: false),
            });
        return pivot;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (!_active || _transitioning || !_showingResults) return;

        var generation = ++_transitionGeneration;
        _layoutGeneration++;
        SetTransitioning(true);
        _showingResults = false;
        UpdateStaleBar();
        ResultsHost.IsHitTestVisible = false;
        // The words were never destroyed, only pushed away, so going back is
        // the reveal played backwards rather than a fresh build. Both halves
        // run at once: the results recede while the cloud comes forward, which
        // is what a camera pulling back looks like.
        BackButton.Visibility = Visibility.Collapsed;
        Heading.Text = UiText.Get("TagCloud_Title.Text");
        ProviderBar.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        CloudHost.Visibility = Visibility.Visible;

        var words = TagElements().ToArray();
        if (words.Length == 0)
        {
            ResultsContent.Content = null;
            ResultsHost.Visibility = Visibility.Collapsed;
            SetTransitioning(false);
            ShowCloud();
            if (ProviderBar.SelectedItem?.Tag is string providerId)
                BuildCloud(_cloud.FirstOrDefault(group => group.ProviderId == providerId),
                    TagCloudMotion.CloudEntryPace.Arrival);
            return;
        }

        // CloudHost has only just become visible, and if the page was navigated
        // away from while the results were up its words were dropped from the
        // visual tree and measure zero until layout runs again — animating them
        // then moves nothing, and the return is a hard cut.
        //
        // One dispatcher hop at normal priority was not enough; measured, the
        // words still reported 0x0. Low priority runs after layout and render
        // have happened, which is the wait this needs — and it asks the layout
        // engine for nothing, where forcing a synchronous pass from a callback
        // is a native re-entrancy hazard.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!_active || generation != _transitionGeneration) return;

            TagCloudMotion.PopOut(ResultsHost, _revealCenter, () =>
            {
                ResultsContent.Content = null;
                ResultsHost.Visibility = Visibility.Collapsed;
            }, () => _active && generation == _transitionGeneration);
            _motion.ReturnToCloud(_chosenTag, words, () =>
            {
                if (!_active || generation != _transitionGeneration) return;
                _chosenTag = null;
                SetTransitioning(false);
                if (_layoutPending) LayoutCloud(entry: null);
                else _motion.StartIdle(words);
            });
        });
    }

    private void SetTransitioning(bool value)
    {
        _transitioning = value;
        CloudPanel.IsHitTestVisible = !value;
        ProviderBar.IsEnabled = !value;
    }

    private void ShowCloud()
    {
        _showingResults = false;
        _chosenTag = null;
        BackButton.Visibility = Visibility.Collapsed;
        Heading.Text = UiText.Get("TagCloud_Title.Text");
        EmptyPanel.Visibility = Visibility.Collapsed;
        ProviderBar.Visibility = Visibility.Visible;
        CloudHost.Visibility = Visibility.Visible;
        ResultsHost.Visibility = Visibility.Collapsed;
        ResultsContent.Content = null;
    }

    private void ShowEmpty(string titleKey, string descriptionKey)
    {
        EmptyTitle.Text = UiText.Get(titleKey);
        EmptyDescription.Text = UiText.Get(descriptionKey);
    }

    private static Point CenterIn(FrameworkElement element, UIElement target)
    {
        try
        {
            return element.TransformToVisual(target)
                .TransformPoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        }
        catch (ArgumentException)
        {
            return new(0, 0);
        }
    }
}

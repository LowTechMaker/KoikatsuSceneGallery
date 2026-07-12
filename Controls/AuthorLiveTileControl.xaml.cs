using System.ComponentModel;
using System.Numerics;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Controls;

public sealed partial class AuthorLiveTileControl : UserControl
{
    private static readonly TimeSpan CycleInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(600);

    private DispatcherQueueTimer? _cycleTimer;
    private int _currentIndex;
    private bool _showingA = true;
    private IReadOnlyList<string> _thumbnailPaths = [];
    private AuthorDisplay? _boundAuthor;
    private readonly Dictionary<string, BitmapImage> _imageCache = [];
    private CompositionEasingFunction? _easing;
    private Vector3KeyFrameAnimation? _inSlide;
    private ScalarKeyFrameAnimation? _inFade;
    private Vector3KeyFrameAnimation? _outSlide;
    private ScalarKeyFrameAnimation? _outFade;

    public AuthorLiveTileControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static readonly DependencyProperty SummaryProperty =
        DependencyProperty.Register(
            nameof(Summary),
            typeof(AuthorSummary),
            typeof(AuthorLiveTileControl),
            new PropertyMetadata(null, OnSummaryChanged));

    public AuthorSummary? Summary
    {
        get => (AuthorSummary?)GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    private static void OnSummaryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AuthorLiveTileControl control)
            control.ApplySummary();
    }

    private void ApplySummary()
    {
        var summary = Summary;
        if (summary is null) return;

        if (summary.Display == _boundAuthor)
        {
            UpdateCounts(summary);
            return;
        }

        StopCycling();
        if (IsLoaded)
            ResetVisuals();

        UnsubscribeAuthorChanges();
        _boundAuthor = summary.Display;
        SubscribeAuthorChanges();

        _imageCache.Clear();
        var liveTilesEnabled = App.Services.GetRequiredService<SettingsViewModel>()?.AuthorLiveTilesEnabled == true;
        _thumbnailPaths = summary.ThumbnailPaths;
        var useLiveTile = liveTilesEnabled && _thumbnailPaths.Count > 0;

        if (useLiveTile)
        {
            ClassicLayout.Visibility = Visibility.Collapsed;
            ThumbnailContainer.Visibility = Visibility.Visible;
            GradientOverlay.Visibility = Visibility.Visible;
            LiveOverlay.Visibility = Visibility.Visible;
            RootGrid.Height = 160;

            ApplyAuthorInfo(summary);

            _currentIndex = 0;
            _showingA = true;
            ImageA.Source = GetOrCreateThumbnail(_thumbnailPaths[0]);
            ImageA.Opacity = 1;
            ImageB.Opacity = 0;

            if (_thumbnailPaths.Count > 1 && IsLoaded)
                StartCycling();
        }
        else
        {
            ThumbnailContainer.Visibility = Visibility.Collapsed;
            GradientOverlay.Visibility = Visibility.Collapsed;
            LiveOverlay.Visibility = Visibility.Collapsed;
            ClassicLayout.Visibility = Visibility.Visible;

            ApplyAuthorInfo(summary);

            ImageA.Source = null;
            ImageB.Source = null;
        }
    }

    private void ApplyAuthorInfo(AuthorSummary summary)
    {
        var display = summary.Display;
        var useLive = ThumbnailContainer.Visibility == Visibility.Visible;

        if (useLive)
        {
            LiveAvatar.DisplayName = display.Name;
            LiveAvatar.ProfilePicture = display.AvatarSource;
            LiveName.Text = display.Name;
            LiveSceneCount.Text = summary.SceneCount.ToString();
            LiveCharacterCount.Text = summary.CharacterCount.ToString();
            LiveCoordinateCount.Text = summary.CoordinateCount.ToString();
        }
        else
        {
            ClassicAvatar.DisplayName = display.Name;
            ClassicAvatar.ProfilePicture = display.AvatarSource;
            ClassicName.Text = display.Name;
            ClassicSceneCount.Text = summary.SceneCount.ToString();
            ClassicCharacterCount.Text = summary.CharacterCount.ToString();
            ClassicCoordinateCount.Text = summary.CoordinateCount.ToString();
        }
    }

    private void UpdateCounts(AuthorSummary summary)
    {
        if (ThumbnailContainer.Visibility == Visibility.Visible)
        {
            LiveSceneCount.Text = summary.SceneCount.ToString();
            LiveCharacterCount.Text = summary.CharacterCount.ToString();
            LiveCoordinateCount.Text = summary.CoordinateCount.ToString();
        }
        else
        {
            ClassicSceneCount.Text = summary.SceneCount.ToString();
            ClassicCharacterCount.Text = summary.CharacterCount.ToString();
            ClassicCoordinateCount.Text = summary.CoordinateCount.ToString();
        }
    }

    private void SubscribeAuthorChanges()
    {
        if (_boundAuthor is not null)
            _boundAuthor.PropertyChanged += OnAuthorPropertyChanged;
    }

    private void UnsubscribeAuthorChanges()
    {
        if (_boundAuthor is not null)
            _boundAuthor.PropertyChanged -= OnAuthorPropertyChanged;
    }

    private void OnAuthorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_boundAuthor is null) return;

        if (e.PropertyName is nameof(AuthorDisplay.Name) or nameof(AuthorDisplay.AvatarSource))
        {
            if (ThumbnailContainer.Visibility == Visibility.Visible)
            {
                LiveAvatar.DisplayName = _boundAuthor.Name;
                LiveAvatar.ProfilePicture = _boundAuthor.AvatarSource;
                LiveName.Text = _boundAuthor.Name;
            }
            else
            {
                ClassicAvatar.DisplayName = _boundAuthor.Name;
                ClassicAvatar.ProfilePicture = _boundAuthor.AvatarSource;
                ClassicName.Text = _boundAuthor.Name;
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ResetVisuals();
        if (_thumbnailPaths.Count > 1 && App.Services.GetRequiredService<SettingsViewModel>()?.AuthorLiveTilesEnabled == true)
            StartCycling();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopCycling();
        UnsubscribeAuthorChanges();
        _boundAuthor = null;
        _imageCache.Clear();
        _easing = null;
        _inSlide = null;
        _inFade = null;
        _outSlide = null;
        _outFade = null;
    }

    private void StartCycling()
    {
        if (_cycleTimer is not null) return;

        _cycleTimer = DispatcherQueue.CreateTimer();
        _cycleTimer.IsRepeating = false;
        _cycleTimer.Interval = CycleInterval + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 4000));
        _cycleTimer.Tick += OnCycleTick;
        _cycleTimer.Start();
    }

    private void StopCycling()
    {
        if (_cycleTimer is null) return;
        _cycleTimer.Stop();
        _cycleTimer.Tick -= OnCycleTick;
        _cycleTimer = null;
    }

    private void OnCycleTick(DispatcherQueueTimer sender, object e)
    {
        _currentIndex = (_currentIndex + 1) % _thumbnailPaths.Count;
        var nextImage = GetOrCreateThumbnail(_thumbnailPaths[_currentIndex]);

        var incoming = _showingA ? ImageB : ImageA;
        var outgoing = _showingA ? ImageA : ImageB;

        incoming.Source = nextImage;
        _showingA = !_showingA;

        AnimateTransition(incoming, outgoing);

        sender.Interval = CycleInterval;
        sender.Start();
    }

    private void EnsureAnimations()
    {
        if (_inSlide is not null) return;

        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _easing ??= compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.4f, 0f),
            new Vector2(0.2f, 1f));

        var h = (float)RootGrid.ActualHeight;

        _inSlide = compositor.CreateVector3KeyFrameAnimation();
        _inSlide.InsertKeyFrame(1f, Vector3.Zero, _easing);
        _inSlide.Duration = AnimationDuration;

        _inFade = compositor.CreateScalarKeyFrameAnimation();
        _inFade.InsertKeyFrame(1f, 1f, _easing);
        _inFade.Duration = AnimationDuration;

        _outSlide = compositor.CreateVector3KeyFrameAnimation();
        _outSlide.InsertKeyFrame(1f, new Vector3(0, -h, 0), _easing);
        _outSlide.Duration = AnimationDuration;

        _outFade = compositor.CreateScalarKeyFrameAnimation();
        _outFade.InsertKeyFrame(1f, 0f, _easing);
        _outFade.Duration = AnimationDuration;
    }

    private void AnimateTransition(UIElement incoming, UIElement outgoing)
    {
        EnsureAnimations();

        var incomingVisual = ElementCompositionPreview.GetElementVisual(incoming);
        var outgoingVisual = ElementCompositionPreview.GetElementVisual(outgoing);

        var h = (float)RootGrid.ActualHeight;
        incomingVisual.Offset = new Vector3(0, h, 0);
        incomingVisual.Opacity = 0;

        incomingVisual.StartAnimation("Offset", _inSlide);
        incomingVisual.StartAnimation("Opacity", _inFade);
        outgoingVisual.StartAnimation("Offset", _outSlide);
        outgoingVisual.StartAnimation("Opacity", _outFade);
    }

    private void ResetVisuals()
    {
        try
        {
            var visualA = ElementCompositionPreview.GetElementVisual(ImageA);
            var visualB = ElementCompositionPreview.GetElementVisual(ImageB);
            visualA.StopAnimation("Offset");
            visualA.StopAnimation("Opacity");
            visualB.StopAnimation("Offset");
            visualB.StopAnimation("Opacity");
            visualA.Offset = Vector3.Zero;
            visualB.Offset = Vector3.Zero;
            visualA.Opacity = 1;
            visualB.Opacity = 0;
        }
        catch (InvalidOperationException ex)
        {
            App.Services.GetRequiredService<IAppLogger>()
                .LogError("AuthorLiveTile.StartAnimation", ex);
        }
    }

    private BitmapImage GetOrCreateThumbnail(string path)
    {
        if (_imageCache.TryGetValue(path, out var cached))
            return cached;
        var img = new BitmapImage(new Uri(path)) { DecodePixelWidth = 280 };
        _imageCache[path] = img;
        return img;
    }
}

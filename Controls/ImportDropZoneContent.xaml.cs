using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The drop-card body shared by the empty-workspace drop zone and the
/// "add more files" overlay, so both keep the same wording and layout.
/// </summary>
public sealed partial class ImportDropZoneContent : UserControl
{
    // Hinged on the box's outer edges, so the left flap swings anticlockwise and
    // the right one clockwise to fall open down each side.
    private const double LeftOpenAngle = -135;
    private const double RightOpenAngle = 135;

    /// <summary>Raised when the user presses the "choose files" button.</summary>
    public event RoutedEventHandler? PickFilesRequested;

    public static readonly DependencyProperty UseBoxArtworkProperty = DependencyProperty.Register(
        nameof(UseBoxArtwork), typeof(bool), typeof(ImportDropZoneContent),
        new PropertyMetadata(false, OnUseBoxArtworkChanged));

    /// <summary>
    /// Swaps the download arrow for the box that <see cref="SealBoxAsync"/> can close.
    /// Only the overlay opts in; the empty-workspace drop zone keeps the arrow.
    /// </summary>
    public bool UseBoxArtwork
    {
        get => (bool)GetValue(UseBoxArtworkProperty);
        set => SetValue(UseBoxArtworkProperty, value);
    }

    public ImportDropZoneContent()
    {
        InitializeComponent();
        OpenBox();
    }

    private static void OnUseBoxArtworkChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (ImportDropZoneContent)sender;
        var useBox = (bool)args.NewValue;
        control.BoxArtwork.Visibility = useBox ? Visibility.Visible : Visibility.Collapsed;
        control.ArrowArtwork.Visibility = useBox ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Splays the flaps back open, ready for the next time the card is shown.</summary>
    public void OpenBox()
    {
        BoxFlapLeftRotate.Angle = LeftOpenAngle;
        BoxFlapRightRotate.Angle = RightOpenAngle;
    }

    /// <summary>Folds both flaps shut, completing once the box is sealed.</summary>
    public Task SealBoxAsync()
    {
        var completion = new TaskCompletionSource();
        var storyboard = new Storyboard();
        storyboard.Children.Add(BuildFlapAnimation(BoxFlapLeftRotate, LeftOpenAngle));
        storyboard.Children.Add(BuildFlapAnimation(BoxFlapRightRotate, RightOpenAngle));
        storyboard.Completed += (_, _) => completion.TrySetResult();
        storyboard.Begin();
        return completion.Task;
    }

    private static DoubleAnimation BuildFlapAnimation(RotateTransform flap, double from)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(260)),
            EnableDependentAnimation = true,
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.4 },
        };
        Storyboard.SetTarget(animation, flap);
        Storyboard.SetTargetProperty(animation, "Angle");
        return animation;
    }

    private void ChooseFiles_Click(object sender, RoutedEventArgs e)
        => PickFilesRequested?.Invoke(this, e);
}

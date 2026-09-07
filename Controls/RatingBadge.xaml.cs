using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Controls;

public sealed partial class RatingBadge : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(RatingBadge),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public RatingBadge()
    {
        InitializeComponent();
        UpdateBadge();
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((RatingBadge)sender).UpdateBadge();

    private void UpdateBadge()
    {
        RestrictedLabel.Text = Text;
        RestrictedBadge.Visibility = Text is "R-18" or "R-18G" ? Visibility.Visible : Visibility.Collapsed;
        GeneralBadge.Visibility = Text == "G" ? Visibility.Visible : Visibility.Collapsed;
    }
}

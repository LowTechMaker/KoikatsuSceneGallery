using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// A page's title row: <see cref="Title"/>, an optional <see cref="Subtitle"/>
/// under it, and <see cref="Actions"/> on the right.
/// </summary>
/// <remarks>
/// Localise it with <c>x:Uid</c> and resource keys ending in <c>.Title</c> and
/// <c>.Subtitle</c>. A page that changes the subtitle at run time (a count, a
/// progress line) sets <see cref="Subtitle"/> from code.
/// </remarks>
public sealed partial class PageHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(""));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(PageHeader), new PropertyMetadata(""));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public PageHeader()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    // An empty line under the title still takes its spacing; collapse it.
    public static Visibility VisibleWhenText(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
}

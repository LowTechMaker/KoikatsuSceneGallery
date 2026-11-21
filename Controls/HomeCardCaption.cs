using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The two lines under a home page card — what it is and who made it — plus
/// the tooltip and the accessible name of the element that opens it. Shared by
/// the shelves and the "this or that" cards so both say the same thing.
/// </summary>
/// <remarks>
/// The text comes from <see cref="HomeViewModel.Describe"/>: the post title
/// (else the artwork folder's, else the file name), the author's name, a few
/// tags and how many other cards the author has. The file name, which used to
/// be the whole caption, is the last and smallest line of the tooltip.
/// </remarks>
internal sealed class HomeCardCaption : StackPanel
{
    private readonly TextBlock _title;
    private readonly TextBlock _author;
    private readonly TextBlock? _extra;

    /// <param name="large">The bigger text of the "this or that" cards.</param>
    public HomeCardCaption(bool large = false)
    {
        Spacing = large ? 2 : 0;
        _title = Line(large ? "BodyStrongTextBlockStyle" : "CaptionTextBlockStyle", "TextFillColorPrimaryBrush");
        _author = Line(large ? "BodyTextBlockStyle" : "CaptionTextBlockStyle", "TextFillColorSecondaryBrush");
        Children.Add(_title);
        Children.Add(_author);
        if (large)
        {
            _extra = Line("CaptionTextBlockStyle", "TextFillColorTertiaryBrush");
            _extra.Visibility = Visibility.Collapsed;
            Children.Add(_extra);
        }
        // The element that opens the card carries "title, author" for screen
        // readers. Raw on this panel alone does not hide its children (seen
        // live: each line showed up again under the tile), so every line is
        // taken out of the tree too.
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    private static TextBlock Line(string style, string brush)
    {
        var line = new TextBlock
        {
            Style = (Style)Application.Current.Resources[style],
            Foreground = (Brush)Application.Current.Resources[brush],
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        AutomationProperties.SetAccessibilityView(line, AccessibilityView.Raw);
        return line;
    }

    /// <summary>Shows <paramref name="info"/>, and gives <paramref name="target"/> its tooltip and accessible name.</summary>
    public void Show(HomeCardInfo info, FrameworkElement target)
    {
        var author = info.Author ?? UiText.Get("SceneGallery_UnknownAuthor");
        _title.Text = info.Title;
        _author.Text = author;
        if (_extra is not null)
        {
            _extra.Text = info.PostOthers > 0 ? UiText.Format("Home_TilePostMore", info.PostOthers) : "";
            _extra.Visibility = info.PostOthers > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        ToolTipService.SetToolTip(target, Tooltip(info, author));
        AutomationProperties.SetName(target, $"{info.Title}, {author}");
    }

    private static StackPanel Tooltip(HomeCardInfo info, string author)
    {
        var panel = new StackPanel { Spacing = 2, MaxWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = info.Title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock { Text = author, TextWrapping = TextWrapping.Wrap });
        if (info.Tags.Count > 0)
            panel.Children.Add(new TextBlock { Text = string.Join("  ·  ", info.Tags), TextWrapping = TextWrapping.Wrap });
        if (info.AuthorOthers >= 1)
            panel.Children.Add(new TextBlock { Text = UiText.Format("Home_TileAuthorMore", info.AuthorOthers), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = info.FileName,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }
}

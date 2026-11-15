using KoikatsuSceneGallery.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// A row of numbered steps: finished ones show a check, the current one is
/// filled with the accent colour, later ones are outlined.
/// </summary>
internal sealed class StepIndicator : StackPanel
{
    private readonly string[] _labels;
    private readonly List<(Border Dot, TextBlock Mark, TextBlock Label)> _steps = [];
    private int _current = -1;

    public StepIndicator(params string[] labels)
    {
        _labels = labels;
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        for (var i = 0; i < labels.Length; i++)
        {
            if (i > 0)
                Children.Add(new Border
                {
                    Width = 20, Height = 1, VerticalAlignment = VerticalAlignment.Center,
                    Background = Brush("ControlStrokeColorDefaultBrush"),
                });
            var mark = new TextBlock
            {
                FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var dot = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = mark };
            var label = new TextBlock { Text = labels[i], VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };
            Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { dot, label } });
            _steps.Add((dot, mark, label));
        }
        SetCurrent(0);
        // The brushes are looked up once; a theme switch has to look them up again.
        ActualThemeChanged += (_, _) => { var current = _current; _current = -1; SetCurrent(current); };
    }

    public void SetCurrent(int current)
    {
        if (current == _current) return;
        _current = current;
        for (var i = 0; i < _steps.Count; i++)
        {
            var (dot, mark, label) = _steps[i];
            var done = i < current;
            var active = i == current;
            dot.Background = active ? Brush("AccentFillColorDefaultBrush") : null;
            dot.BorderBrush = active || done ? Brush("AccentFillColorDefaultBrush") : Brush("ControlStrongStrokeColorDefaultBrush");
            mark.Text = done ? "" : (i + 1).ToString();
            mark.FontFamily = done ? (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"] : FontFamily.XamlAutoFontFamily;
            mark.Foreground = active ? Brush("TextOnAccentFillColorPrimaryBrush") : done ? Brush("AccentTextFillColorPrimaryBrush") : Brush("TextFillColorSecondaryBrush");
            label.Foreground = active ? Brush("TextFillColorPrimaryBrush") : Brush("TextFillColorSecondaryBrush");
            label.FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }

        var name = current >= 0 && current < _labels.Length
            ? UiText.Format("Steps_Position", current + 1, _labels.Length, _labels[current])
            : "";
        AutomationProperties.SetName(this, name);
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        if (FrameworkElementAutomationPeer.FromElement(this) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}

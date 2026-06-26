using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace KoikatsuSceneGallery.Controls;

public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0.0, (d, _) => ((WrapPanel)d).InvalidateMeasure()));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var spacing = Spacing;
        var maxWidth = double.IsInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : availableSize.Width;

        double lineWidth = 0;
        double lineHeight = 0;
        double totalWidth = 0;
        double totalHeight = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(maxWidth, availableSize.Height));
            var desired = child.DesiredSize;
            var childWidth = desired.Width;
            var widthWithSpacing = lineWidth > 0 ? childWidth + spacing : childWidth;

            if (lineWidth > 0 && lineWidth + widthWithSpacing > maxWidth)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineHeight + spacing;
                lineWidth = childWidth;
                lineHeight = desired.Height;
            }
            else
            {
                lineWidth += widthWithSpacing;
                lineHeight = Math.Max(lineHeight, desired.Height);
            }
        }

        totalWidth = Math.Max(totalWidth, lineWidth);
        totalHeight += lineHeight;
        return new Size(totalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var spacing = Spacing;
        double x = 0;
        double y = 0;
        double lineHeight = 0;

        foreach (var child in Children)
        {
            var desired = child.DesiredSize;
            var widthWithSpacing = x > 0 ? desired.Width + spacing : desired.Width;

            if (x > 0 && x + widthWithSpacing > finalSize.Width)
            {
                x = 0;
                y += lineHeight + spacing;
                lineHeight = 0;
            }

            child.Arrange(new Rect(x, y, desired.Width, desired.Height));
            x += desired.Width + spacing;
            lineHeight = Math.Max(lineHeight, desired.Height);
        }

        return finalSize;
    }
}

using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace KoikatsuSceneGallery.Controls;

public sealed class WrapPanel : Panel
{
    public double Spacing { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
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
            var widthWithSpacing = lineWidth > 0 ? childWidth + Spacing : childWidth;

            if (lineWidth > 0 && lineWidth + widthWithSpacing > maxWidth)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineHeight + Spacing;
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
        double x = 0;
        double y = 0;
        double lineHeight = 0;

        foreach (var child in Children)
        {
            var desired = child.DesiredSize;
            var widthWithSpacing = x > 0 ? desired.Width + Spacing : desired.Width;

            if (x > 0 && x + widthWithSpacing > finalSize.Width)
            {
                x = 0;
                y += lineHeight + Spacing;
                lineHeight = 0;
            }

            child.Arrange(new Rect(x, y, desired.Width, desired.Height));
            x += desired.Width + Spacing;
            lineHeight = Math.Max(lineHeight, desired.Height);
        }

        return finalSize;
    }
}

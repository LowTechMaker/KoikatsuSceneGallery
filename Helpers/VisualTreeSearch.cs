using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace KoikatsuSceneGallery.Helpers;

internal static class VisualTreeSearch
{
    public static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindDescendant<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    public static T? FindDescendantByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && match.Name == name) return match;
            var result = FindDescendantByName<T>(child, name);
            if (result is not null) return result;
        }
        return null;
    }
}

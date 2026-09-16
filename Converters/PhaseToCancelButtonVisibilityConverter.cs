using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace KoikatsuSceneGallery.Converters;

/// <summary>
/// Keeps cancellation available only while a transaction is still executing
/// forward work. Rollback is intentionally non-interruptible.
/// </summary>
public sealed class PhaseToCancelButtonVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is ImportExecutionPhase.Executing
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

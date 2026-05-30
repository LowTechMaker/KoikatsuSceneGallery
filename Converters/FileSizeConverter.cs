using Microsoft.UI.Xaml.Data;

namespace KoikatsuSceneGallery.Converters;

public class FileSizeConverter : IValueConverter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB"];

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not long bytes) return "0 B";

        var size = (double)bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < Units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:F1} {Units[unitIndex]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

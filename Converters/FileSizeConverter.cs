using Microsoft.UI.Xaml.Data;

namespace KoikatsuSceneGallery.Converters;

public class FileSizeConverter : IValueConverter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB"];

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is long bytes ? Format(bytes) : "0 B";

    /// <summary>A byte count as "1.5 MB", for code that is not a binding.</summary>
    public static string Format(long bytes)
    {
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

using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Controls;

public sealed class ImportReviewTemplateSelector : DataTemplateSelector
{
    public DataTemplate SectionTemplate { get; set; } = null!;
    public DataTemplate GroupTemplate { get; set; } = null!;
    public DataTemplate ListTemplate { get; set; } = null!;
    public DataTemplate GridTemplate { get; set; } = null!;
    protected override DataTemplate SelectTemplateCore(object item) => ((ImportReviewRow)item).Kind switch
    {
        ImportReviewRowKind.Section => SectionTemplate,
        ImportReviewRowKind.Group => GroupTemplate,
        ImportReviewRowKind.ListItem => ListTemplate,
        _ => GridTemplate
    };
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}

using KoikatsuSceneGallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// Lets the "add a source" cell sit in the same grid as the sources, so it
/// lays out as the last cell of the wrap rather than beside or below it.
/// </summary>
public sealed class LocalSourceTileTemplateSelector : DataTemplateSelector
{
    public DataTemplate SourceTemplate { get; set; } = null!;

    public DataTemplate AddTemplate { get; set; } = null!;

    protected override DataTemplate SelectTemplateCore(object item)
        => item is AddLocalSourceTile ? AddTemplate : SourceTemplate;

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}

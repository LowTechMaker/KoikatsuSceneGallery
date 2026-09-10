using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.ViewModels;

public partial class MetadataFilterState : ObservableObject
{
    [ObservableProperty] public partial int? Sex { get; set; }
    [ObservableProperty] public partial int? Personality { get; set; }
    [ObservableProperty] public partial string? PluginGuid { get; set; }
    public int ActiveCount => (Sex is null ? 0 : 1) + (Personality is null ? 0 : 1) + (string.IsNullOrEmpty(PluginGuid) ? 0 : 1);
    public bool Matches(CardMetadataSummary? summary) => CardMetadataQuery.PassesFilters(summary, Sex, Personality, PluginGuid);
}

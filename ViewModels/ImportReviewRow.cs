using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.ViewModels;

public sealed partial class ImportReviewRow(string key, ImportReviewRowKind kind) : ObservableObject
{
    public string Key { get; } = key;
    public ImportReviewRowKind Kind { get; } = kind;
    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string Description { get; set; } = string.Empty;
    [ObservableProperty] public partial IReadOnlyList<ImportItemReviewState> Items { get; set; } = [];
    public ImportItemReviewState? Item => Items.FirstOrDefault();
    partial void OnItemsChanged(IReadOnlyList<ImportItemReviewState> value) => OnPropertyChanged(nameof(Item));
}

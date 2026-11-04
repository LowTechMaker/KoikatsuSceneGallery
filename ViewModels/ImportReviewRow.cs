using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.ViewModels;

public sealed partial class ImportReviewRow(string key, ImportReviewRowKind kind) : ObservableObject
{
    public string Key { get; } = key;
    public ImportReviewRowKind Kind { get; } = kind;
    public ImportReviewSection Section { get; init; }
    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string Description { get; set; } = string.Empty;
    [ObservableProperty] public partial IReadOnlyList<ImportItemReviewState> Items { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<ModReviewItem> Mods { get; set; } = [];
    [ObservableProperty] public partial bool IsCollapsed { get; set; }

    public ImportItemReviewState? Item => Items.FirstOrDefault();
    public ModReviewItem? Mod => Mods.FirstOrDefault();
    public bool HasSelectableItems => Section == ImportReviewSection.Mods
        ? Mods.Any(mod => mod.CanInstall)
        : Items.Any(item => item.CanSelect);

    /// <summary>
    /// The header's three-state box: every selectable member selected (for mods: to be
    /// installed), none, or some (null).
    /// </summary>
    public bool? SelectionState
    {
        get
        {
            int total, chosen;
            if (Section == ImportReviewSection.Mods)
            {
                var installable = Mods.Where(mod => mod.CanInstall).ToArray();
                total = installable.Length;
                chosen = installable.Count(mod => mod.Install);
            }
            else
            {
                var selectable = Items.Where(item => item.CanSelect).ToArray();
                total = selectable.Length;
                chosen = selectable.Count(item => item.IsSelected);
            }
            return chosen == 0 ? false : chosen == total ? true : null;
        }
    }

    /// <summary>Chevron glyph: pointing down when open, right when collapsed.</summary>
    public string ChevronGlyph => IsCollapsed ? "" : "";

    partial void OnItemsChanged(IReadOnlyList<ImportItemReviewState> value)
    {
        OnPropertyChanged(nameof(Item));
        RefreshSelectionAvailability();
    }

    partial void OnModsChanged(IReadOnlyList<ModReviewItem> value)
    {
        OnPropertyChanged(nameof(Mod));
        RefreshSelectionAvailability();
    }

    partial void OnIsCollapsedChanged(bool value) => OnPropertyChanged(nameof(ChevronGlyph));

    public void RefreshSelectionAvailability()
    {
        OnPropertyChanged(nameof(HasSelectableItems));
        OnPropertyChanged(nameof(SelectionState));
    }
}

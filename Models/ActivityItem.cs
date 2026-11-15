using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace KoikatsuSceneGallery.Models;

/// <summary>One piece of background work shown in the title bar's activity list.</summary>
public sealed partial class ActivityItem(string id) : ObservableObject
{
    public string Id { get; } = id;

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailVisibility))]
    public partial string Detail { get; set; } = "";

    /// <summary>0 to 1, or null while there is no honest number to show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate), nameof(ProgressValue))]
    public partial double? Progress { get; set; }

    /// <summary>Stops the work, or null when it cannot be stopped from here.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CancelVisibility))]
    public partial Action? Cancel { get; set; }

    public bool IsIndeterminate => Progress is null;
    public double ProgressValue => (Progress ?? 0) * 100;
    public Visibility DetailVisibility => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CancelVisibility => Cancel is null ? Visibility.Collapsed : Visibility.Visible;
}

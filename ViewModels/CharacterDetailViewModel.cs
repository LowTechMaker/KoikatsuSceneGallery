using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.ViewModels;

public partial class CharacterDetailViewModel : ObservableObject
{
    [ObservableProperty]
    public partial CharacterCard? Card { get; set; }

    /// <summary>True once the opened card's metadata has been parsed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParsing))]
    public partial bool MetadataLoaded { get; set; }

    public bool IsParsing => !MetadataLoaded;

    [ObservableProperty]
    public partial string FullName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Nickname { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SexDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GameDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MadevilDisplay { get; set; } = string.Empty;
}

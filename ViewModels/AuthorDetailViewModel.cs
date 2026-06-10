using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

public partial class AuthorDetailViewModel : ObservableObject
{
    [ObservableProperty]
    public partial AuthorDisplay? Author { get; set; }

    public ObservableCollection<SceneCard> Scenes { get; } = [];
    public ObservableCollection<CharacterCard> Characters { get; } = [];
    public ObservableCollection<CoordinateCard> Coordinates { get; } = [];

    [ObservableProperty]
    public partial int SceneCount { get; set; }

    [ObservableProperty]
    public partial int CharacterCount { get; set; }

    [ObservableProperty]
    public partial int CoordinateCount { get; set; }

    public void Load(AuthorSummary summary)
    {
        Author = summary.Display;
        var key = summary.Display.Key;

        Scenes.Clear();
        foreach (var card in App.GalleryViewModel.Cards)
        {
            if (card.Author?.Key == key)
                Scenes.Add(card);
        }
        SceneCount = Scenes.Count;

        Characters.Clear();
        foreach (var card in App.CharacterGalleryViewModel.Cards)
        {
            if (card.Author?.Key == key)
                Characters.Add(card);
        }
        CharacterCount = Characters.Count;

        Coordinates.Clear();
        foreach (var card in App.CoordinateGalleryViewModel.Cards)
        {
            if (card.Author?.Key == key)
                Coordinates.Add(card);
        }
        CoordinateCount = Coordinates.Count;
    }
}

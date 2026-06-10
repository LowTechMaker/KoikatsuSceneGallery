using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.ViewModels;

public partial class DetailViewModel : ObservableObject
{
    private static readonly Regex PixivIdPattern = new(@"(\d{6,})_p\d+", RegexOptions.Compiled);
    private static readonly Regex BepisDbPattern = new(@"(KKSCENE|KKCLOTHING|KK)_(\d+)", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> BepisDbPrefixMap = new()
    {
        ["KKSCENE"] = "kkscenes",
        ["KKCLOTHING"] = "kkclothing",
        ["KK"] = "koikatsu",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PixivArtworkId))]
    [NotifyPropertyChangedFor(nameof(HasPixivArtworkId))]
    [NotifyPropertyChangedFor(nameof(BepisDbId))]
    [NotifyPropertyChangedFor(nameof(BepisDbUrl))]
    [NotifyPropertyChangedFor(nameof(HasBepisDbUrl))]
    public partial SceneCard? Card { get; set; }

    public string? PixivArtworkId
    {
        get
        {
            if (Card is null) return null;
            var name = System.IO.Path.GetFileNameWithoutExtension(Card.FilePath);
            var match = PixivIdPattern.Match(name);
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    public bool HasPixivArtworkId => PixivArtworkId != null;

    public string? BepisDbId
    {
        get
        {
            if (Card is null) return null;
            var name = System.IO.Path.GetFileNameWithoutExtension(Card.FilePath);
            var match = BepisDbPattern.Match(name);
            return match.Success ? match.Groups[0].Value : null;
        }
    }

    public string? BepisDbUrl
    {
        get
        {
            if (Card is null) return null;
            var name = System.IO.Path.GetFileNameWithoutExtension(Card.FilePath);
            var match = BepisDbPattern.Match(name);
            if (!match.Success) return null;
            var category = BepisDbPrefixMap[match.Groups[1].Value];
            var id = long.Parse(match.Groups[2].Value);
            return $"https://db.bepis.moe/{category}/view/{id}";
        }
    }

    public bool HasBepisDbUrl => BepisDbUrl != null;
}

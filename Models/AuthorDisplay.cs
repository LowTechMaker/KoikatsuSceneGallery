using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Models;

/// <summary>
/// Observable author info shared by every card from the same author: one
/// instance per <see cref="AuthorKey"/>, so when the fetched name/avatar
/// lands, a single property change updates all bound cards, and only one
/// decoded avatar bitmap ever exists per author. Mutated on the UI thread only.
/// </summary>
public partial class AuthorDisplay : ObservableObject
{
    public AuthorDisplay(AuthorKey key, string initialName, string profileUrl)
    {
        Key = key;
        Name = initialName;
        ProfileUrl = profileUrl;
    }

    public AuthorKey Key { get; }

    public string ProfileUrl { get; }

    /// <summary>
    /// Whether this author has a page worth opening. False for a local source,
    /// which is resolved entirely from disk, so callers must gate their "open
    /// profile" affordance on it rather than launching an empty Uri.
    /// </summary>
    public bool HasProfileUrl => !string.IsNullOrWhiteSpace(ProfileUrl);

    /// <summary>Folder-derived name at first, replaced by the fetched profile name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvatarSource))]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    public partial string? AvatarPath { get; set; }

    public bool HasAvatar => AvatarPath != null;

    /// <summary>
    /// Whether this author is a locally collected source rather than a remote
    /// provider. Gates the affordances only a local source has, such as
    /// editing its name and picture.
    /// </summary>
    public bool IsLocalSource => LocalSourceIdentity.IsLocal(Key.ProviderId);

    private BitmapImage? _avatarSource;
    /// <summary>
    /// Stable BitmapImage for avatar binding. UI thread only.
    /// </summary>
    public BitmapImage? AvatarSource
    {
        get
        {
            if (AvatarPath is null) return null;
            return _avatarSource ??= new BitmapImage(new Uri(AvatarPath)) { DecodePixelWidth = 96 };
        }
    }

    partial void OnAvatarPathChanged(string? value) => _avatarSource = null;
}

/// <summary>Implemented by card models that can carry an author badge.</summary>
public interface IAuthorOwner
{
    string FilePath { get; }
    DateTime DateModified { get; }
    AuthorDisplay? Author { get; set; }
}

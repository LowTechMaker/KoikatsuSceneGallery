using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.ViewModels;

public sealed class FriendListItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarPath { get; set; }
    public string? FolderPath { get; set; }
    public bool HasFolder => !string.IsNullOrWhiteSpace(FolderPath);
    public bool HasNoFolder => !HasFolder;
    public string MoreAutomationId => $"FriendMore_{Id:N}";
    public string RenameAutomationId => $"FriendRename_{Id:N}";
    public string DeleteAutomationId => $"FriendDelete_{Id:N}";
}

public sealed class SelfProfileListItemViewModel
{
    public string Name { get; set; } = string.Empty;
    public string? AvatarPath { get; set; }
    public string? FolderPath { get; set; }
    public bool HasFolder => !string.IsNullOrWhiteSpace(FolderPath);
    public bool HasNoFolder => !HasFolder;
}

public sealed class FriendCardItemViewModel
{
    public CardBase Card { get; set; } = null!;
    public string AutomationIdSuffix { get; set; } = string.Empty;
    public string FileName => Card.FileName;
    public string FilePath => Card.FilePath;
    public string MoreAutomationId =>
        $"FriendCardMore_{AutomationIdSuffix}";
    public string RemoveAutomationId =>
        $"FriendRemoveCard_{AutomationIdSuffix}";
    public bool IsOldVersion =>
        Card is CharacterCard { VariantKind: CharacterVariantKind.OldVersion };
    public bool IsAlternative =>
        Card is CharacterCard { VariantKind: CharacterVariantKind.Alternative };
}

public sealed class FriendAuthorAvatarItemViewModel(
    AuthorDisplay author,
    string automationId)
{
    public AuthorDisplay Author { get; } = author;
    public string Name => Author.Name;
    public string ProfileUrl => Author.ProfileUrl;
    public BitmapImage? AvatarSource => Author.AvatarSource;
    public string AutomationId { get; } = automationId;

    public override string ToString() => Name;
}

public partial class FriendsViewModel : ObservableObject, IDisposable
{
    private readonly FriendService _friendService;
    private readonly DispatcherQueue _dispatcherQueue;
    private SelfProfileListItemViewModel _selfProfile = null!;

    public ObservableCollection<FriendListItemViewModel> Friends { get; } = [];
    public SelfProfileListItemViewModel SelfProfile
    {
        get => _selfProfile;
        private set
        {
            if (!SetProperty(ref _selfProfile, value))
                return;

            OnPropertyChanged(nameof(SelfProfileName));
            OnPropertyChanged(nameof(SelfProfileAvatarPath));
            OnPropertyChanged(nameof(SelfProfileFolderPath));
            OnPropertyChanged(nameof(SelfProfileHasFolder));
            OnPropertyChanged(nameof(SelfProfileHasNoFolder));
        }
    }

    public string SelfProfileName => SelfProfile.Name;
    public string? SelfProfileAvatarPath => SelfProfile.AvatarPath;
    public string? SelfProfileFolderPath => SelfProfile.FolderPath;
    public bool SelfProfileHasFolder => SelfProfile.HasFolder;
    public bool SelfProfileHasNoFolder => SelfProfile.HasNoFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string SearchText { get; set; } = string.Empty;

    public bool IsEmpty => Friends.Count == 0;

    public FriendsViewModel(
        FriendService friendService,
        DispatcherQueue dispatcherQueue)
    {
        _friendService = friendService;
        _dispatcherQueue = dispatcherQueue;
        _friendService.FriendChanged += OnFriendChanged;
        _friendService.SelfProfileChanged += OnSelfProfileChanged;
        RefreshSelfProfile();
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    public void Activate()
    {
        RefreshSelfProfile();
        Refresh();
    }

    private void OnFriendChanged(FriendChange change)
    {
        if (_dispatcherQueue.HasThreadAccess)
            ApplyChange(change);
        else
            _dispatcherQueue.TryEnqueue(() => ApplyChange(change));
    }

    private void OnSelfProfileChanged(FriendChangeKinds kinds)
    {
        if (_dispatcherQueue.HasThreadAccess)
            RefreshSelfProfile();
        else
            _dispatcherQueue.TryEnqueue(RefreshSelfProfile);
    }

    private void ApplyChange(FriendChange change)
    {
        if (change.Kinds.HasFlag(FriendChangeKinds.Collection)
            || change.Kinds.HasFlag(FriendChangeKinds.Identity))
        {
            Refresh();
            return;
        }

        if (!change.Kinds.HasFlag(FriendChangeKinds.Avatar)
            && !change.Kinds.HasFlag(FriendChangeKinds.Folders))
        {
            return;
        }

        var friend = _friendService.Find(change.FriendId);
        var index = FindIndex(change.FriendId);
        if (friend is null || index < 0)
        {
            Refresh();
            return;
        }

        Friends[index] = CreateListItem(friend);
    }

    private void Refresh()
    {
        var query = SearchText.Trim();
        var filtered = _friendService.Friends
            .Where(friend =>
                query.Length == 0
                || friend.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(CreateListItem)
            .ToList();

        Friends.Clear();
        foreach (var friend in filtered)
            Friends.Add(friend);

        OnPropertyChanged(nameof(IsEmpty));
    }

    private FriendListItemViewModel CreateListItem(FriendRecord friend) =>
        new()
        {
            Id = friend.Id,
            Name = friend.Name,
            AvatarPath = _friendService.GetAvatarPath(friend),
            FolderPath = _friendService.GetPrimaryFolder(friend),
        };

    private void RefreshSelfProfile()
    {
        var profile = _friendService.SelfProfile;
        SelfProfile = new SelfProfileListItemViewModel
        {
            Name = profile.Name,
            AvatarPath = _friendService.GetAvatarPath(profile),
            FolderPath = _friendService.GetPrimaryFolder(profile),
        };
    }

    private int FindIndex(Guid friendId)
    {
        for (var index = 0; index < Friends.Count; index++)
        {
            if (Friends[index].Id == friendId)
                return index;
        }

        return -1;
    }

    public void Dispose()
    {
        _friendService.FriendChanged -= OnFriendChanged;
        _friendService.SelfProfileChanged -= OnSelfProfileChanged;
        GC.SuppressFinalize(this);
    }
}

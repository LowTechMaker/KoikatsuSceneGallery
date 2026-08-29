using System.Collections.ObjectModel;
using KoikatsuSceneGallery.Models;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

public sealed record FriendFolderRoots(
    string? SceneRootPath,
    string? CharacterRootPath,
    string? CoordinateRootPath);

public sealed record SelfProfileFolderBindings(
    string? SceneFolderPath,
    string? CharacterFolderPath,
    string? CoordinateFolderPath);

[Flags]
public enum FriendChangeKinds
{
    None = 0,
    Collection = 1,
    Identity = 2,
    Avatar = 4,
    Folders = 8,
    Cards = 16,
}

public readonly record struct FriendChange(
    Guid FriendId,
    FriendChangeKinds Kinds);

[Flags]
public enum FriendLibrarySourceKinds
{
    None = 0,
    Scene = 1,
    Character = 2,
    Coordinate = 4,
    All = Scene | Character | Coordinate,
}

/// <summary>
/// The friend-owned library sources at one point in time, with every folder
/// and card already checked against the filesystem. Consumers can build source
/// sets from it without touching the disk again.
/// </summary>
public sealed record LinkedLibrarySnapshot(
    IReadOnlyList<string> SceneFolders,
    IReadOnlyList<string> CharacterFolders,
    IReadOnlyList<string> CoordinateFolders,
    IReadOnlyList<string> SceneCardPaths,
    IReadOnlyList<string> CharacterCardPaths,
    IReadOnlyList<string> CoordinateCardPaths);

public sealed class FriendService
{
    private readonly record struct LinkedCardClassification(
        long Length,
        long LastWriteTimeUtcTicks,
        CardType CardType);

    private readonly record struct SelfFolderTarget(
        CardType CardType,
        string ConfiguredRoot,
        string FolderPath);

    private readonly FriendStore _store;
    private readonly SelfProfileStore _selfProfileStore;
    private readonly FriendAvatarStorage _avatarStorage;
    private readonly AuthorInfoService _authorInfoService;
    private readonly IAppLogger _logger;
    private readonly string _defaultSelfProfileName;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly object _linkedCardClassificationLock = new();
    private readonly Dictionary<string, LinkedCardClassification>
        _linkedCardClassifications = new(StringComparer.OrdinalIgnoreCase);

    public FriendService(
        IAppLogger logger,
        AuthorInfoService authorInfoService,
        string selfProfileDefaultName = "Me")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selfProfileDefaultName);
        _logger = logger;
        _authorInfoService = authorInfoService;
        _defaultSelfProfileName = selfProfileDefaultName.Trim();
        _store = new FriendStore(Path.Combine(AppPaths.LocalFolder, "friends.json"));
        _selfProfileStore = new SelfProfileStore(
            Path.Combine(AppPaths.LocalFolder, "self-profile.json"));
        _avatarStorage = new FriendAvatarStorage(
            Path.Combine(AppPaths.LocalFolder, "friend-avatars"));
        _authorInfoService.AuthorProfilesChanged += OnAuthorProfileChanged;
    }

    public ObservableCollection<FriendRecord> Friends { get; } = [];
    public SelfProfile SelfProfile { get; private set; } = new();

    public event Action<FriendChange>? FriendChanged;
    public event Action<FriendChangeKinds>? SelfProfileChanged;
    public event Action<FriendLibrarySourceKinds>? LibrarySourcesChanged;

    public async Task LoadAsync()
    {
        await LoadFriendsAsync();
        await LoadSelfProfileAsync();
    }

    private async Task LoadFriendsAsync()
    {
        try
        {
            var friends = await _store.LoadAsync();
            var repairedStoredData = false;
            var usedIds = new HashSet<Guid>();
            var repairedFriends = new List<FriendRecord>(friends.Count);
            foreach (var friend in friends)
            {
                if (friend is null)
                {
                    repairedStoredData = true;
                    _logger.LogError(
                        "Friends.Load.InvalidRecord",
                        new FormatException(
                            "The stored friend entry was null."));
                    continue;
                }

                repairedStoredData |= FriendRecordRepair.Repair(
                    friend,
                    usedIds,
                    (path, ex) => _logger.LogError(
                        "Friends.Load.InvalidPath",
                        ex,
                        path));
                repairedFriends.Add(friend);
            }

            Friends.Clear();
            foreach (var friend in repairedFriends.OrderBy(
                         friend => friend.Name,
                         StringComparer.CurrentCultureIgnoreCase))
                Friends.Add(friend);

            if (repairedStoredData)
                await SaveAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Friends.Load", ex);
        }
    }

    private async Task LoadSelfProfileAsync()
    {
        try
        {
            var storedProfile = await _selfProfileStore.LoadAsync();
            var shouldSave = storedProfile is null;
            SelfProfile = storedProfile ?? new SelfProfile
            {
                Name = _defaultSelfProfileName,
            };
            shouldSave |= SelfProfileRepair.Repair(
                SelfProfile,
                _defaultSelfProfileName,
                (path, ex) => _logger.LogError(
                    "SelfProfile.Load.InvalidPath",
                    ex,
                    path));
            if (shouldSave)
                await SaveSelfProfileAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("SelfProfile.Load", ex);
            if (string.IsNullOrWhiteSpace(SelfProfile.Name))
                SelfProfile.Name = _defaultSelfProfileName;
        }
    }

    public async Task<FriendRecord> AddAsync(
        string name,
        FriendFolderRoots? roots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmedName = name.Trim();
        var createdFolders = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        FriendRecord? friend = null;
        try
        {
            await ExecuteMutationAsync(async () =>
            {
                friend = new FriendRecord
                {
                    Name = trimmedName,
                };
                friend.SceneFolderPath = CreatePersonalFolder(
                    roots?.SceneRootPath,
                    trimmedName,
                    friend.Id,
                    createCharacterVariantFolders: false,
                    createdFolders);
                friend.CharacterFolderPath = CreatePersonalFolder(
                    roots?.CharacterRootPath,
                    trimmedName,
                    friend.Id,
                    createCharacterVariantFolders: true,
                    createdFolders);
                friend.CoordinateFolderPath = CreatePersonalFolder(
                    roots?.CoordinateRootPath,
                    trimmedName,
                    friend.Id,
                    createCharacterVariantFolders: false,
                    createdFolders);

                Friends.Add(friend);
                SortFriends();
                await SaveAsync();
            });
        }
        catch
        {
            TryRemoveEmptyCreatedFolders(createdFolders);
            throw;
        }

        if (friend is null)
            throw new InvalidOperationException("The friend was not created.");

        NotifyFriendChanged(friend.Id, FriendChangeKinds.Collection);
        return friend;
    }

    public async Task RenameAsync(Guid id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await ExecuteMutationAsync(async () =>
        {
            var friend = GetRequired(id);
            friend.Name = name.Trim();
            SortFriends();
            await SaveAsync();
        });
        NotifyFriendChanged(id, FriendChangeKinds.Identity);
    }

    public async Task RenameSelfProfileAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await ExecuteSelfProfileMutationAsync(async () =>
        {
            SelfProfile.Name = name.Trim();
            await SaveSelfProfileAsync();
        });
        NotifySelfProfileChanged(FriendChangeKinds.Identity);
    }

    public async Task SetLocalAvatarAsync(Guid id, string sourcePath)
    {
        var avatarPath = await _avatarStorage.ImportAsync(id, sourcePath);
        string? previousPath = null;
        try
        {
            await ExecuteMutationAsync(async () =>
            {
                var friend = GetRequired(id);
                previousPath = friend.AvatarPath;
                friend.AvatarPath = avatarPath;
                friend.AvatarAuthorProviderId = null;
                friend.AvatarAuthorId = null;
                await SaveAsync();
            });
        }
        catch
        {
            TryRemoveManagedAvatar(
                avatarPath,
                "Friends.Avatar.RollbackImported");
            throw;
        }

        TryRemoveManagedAvatar(previousPath, "Friends.Avatar.RemovePrevious");
        NotifyFriendChanged(id, FriendChangeKinds.Avatar);
    }

    public async Task SetSelfProfileLocalAvatarAsync(string sourcePath)
    {
        var avatarPath = await _avatarStorage.ImportAsync(
            SelfProfile.Id,
            sourcePath);
        string? previousPath = null;
        try
        {
            await ExecuteSelfProfileMutationAsync(async () =>
            {
                previousPath = SelfProfile.AvatarPath;
                SelfProfile.AvatarPath = avatarPath;
                SelfProfile.AvatarAuthorProviderId = null;
                SelfProfile.AvatarAuthorId = null;
                await SaveSelfProfileAsync();
            });
        }
        catch
        {
            TryRemoveManagedAvatar(
                avatarPath,
                "SelfProfile.Avatar.RollbackImported");
            throw;
        }

        TryRemoveManagedAvatar(
            previousPath,
            "SelfProfile.Avatar.RemovePrevious");
        NotifySelfProfileChanged(FriendChangeKinds.Avatar);
    }

    public async Task SetAuthorAvatarAsync(Guid id, AuthorDisplay author)
    {
        ArgumentNullException.ThrowIfNull(author);
        string? previousPath = null;
        await ExecuteMutationAsync(async () =>
        {
            var friend = GetRequired(id);
            previousPath = friend.AvatarPath;
            friend.AvatarPath = author.AvatarPath;
            friend.AvatarAuthorProviderId = author.Key.ProviderId;
            friend.AvatarAuthorId = author.Key.Id;
            await SaveAsync();
        });
        TryRemoveManagedAvatar(previousPath, "Friends.Avatar.RemovePrevious");
        NotifyFriendChanged(id, FriendChangeKinds.Avatar);
    }

    public async Task SetSelfProfileAuthorAvatarAsync(AuthorDisplay author)
    {
        ArgumentNullException.ThrowIfNull(author);
        string? previousPath = null;
        await ExecuteSelfProfileMutationAsync(async () =>
        {
            previousPath = SelfProfile.AvatarPath;
            SelfProfile.AvatarPath = author.AvatarPath;
            SelfProfile.AvatarAuthorProviderId = author.Key.ProviderId;
            SelfProfile.AvatarAuthorId = author.Key.Id;
            await SaveSelfProfileAsync();
        });
        TryRemoveManagedAvatar(
            previousPath,
            "SelfProfile.Avatar.RemovePrevious");
        NotifySelfProfileChanged(FriendChangeKinds.Avatar);
    }

    public async Task ClearAvatarAsync(Guid id)
    {
        string? previousPath = null;
        var changed = await ExecuteMutationAsync(async () =>
        {
            var friend = GetRequired(id);
            previousPath = friend.AvatarPath;
            if (previousPath is null
                && friend.AvatarAuthorProviderId is null
                && friend.AvatarAuthorId is null)
            {
                return false;
            }

            friend.AvatarPath = null;
            friend.AvatarAuthorProviderId = null;
            friend.AvatarAuthorId = null;
            await SaveAsync();
            return true;
        });
        if (!changed)
            return;

        TryRemoveManagedAvatar(previousPath, "Friends.Avatar.RemoveCleared");
        NotifyFriendChanged(id, FriendChangeKinds.Avatar);
    }

    public async Task ClearSelfProfileAvatarAsync()
    {
        string? previousPath = null;
        var changed = await ExecuteSelfProfileMutationAsync(async () =>
        {
            previousPath = SelfProfile.AvatarPath;
            if (previousPath is null
                && SelfProfile.AvatarAuthorProviderId is null
                && SelfProfile.AvatarAuthorId is null)
            {
                return false;
            }

            SelfProfile.AvatarPath = null;
            SelfProfile.AvatarAuthorProviderId = null;
            SelfProfile.AvatarAuthorId = null;
            await SaveSelfProfileAsync();
            return true;
        });
        if (!changed)
            return;

        TryRemoveManagedAvatar(
            previousPath,
            "SelfProfile.Avatar.RemoveCleared");
        NotifySelfProfileChanged(FriendChangeKinds.Avatar);
    }

    public async Task<string> SetFolderAsync(
        Guid id,
        CardType cardType,
        string configuredRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredRoot);
        string? folderPath = null;
        var folderExisted = true;
        var createdFolders = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        bool changed;
        try
        {
            changed = await ExecuteMutationAsync(async () =>
            {
                var friend = GetRequired(id);
                var expectedFolderPath =
                    FriendFolderLayout.GetPersonalFolderPath(
                        configuredRoot,
                        friend.Name);
                folderExisted = Directory.Exists(expectedFolderPath);
                folderPath = CreatePersonalFolder(
                    configuredRoot,
                    friend.Name,
                    friend.Id,
                    cardType == CardType.Character,
                    createdFolders);
                if (string.Equals(
                        GetFolder(friend, cardType),
                        folderPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                SetFolderPath(friend, cardType, folderPath);
                await SaveAsync();
                return true;
            });
        }
        catch
        {
            TryRemoveEmptyCreatedFolders(createdFolders);
            throw;
        }

        if (folderPath is null)
            throw new InvalidOperationException("The friend folder was not created.");

        if (changed || createdFolders.Count > 0)
            NotifyFriendChanged(id, FriendChangeKinds.Folders);
        if (changed || !folderExisted)
            NotifyLibrarySourcesChanged(GetSourceKind(cardType));
        return folderPath;
    }

    public async Task<string> SetSelfProfileFolderAsync(
        CardType cardType,
        string configuredRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredRoot);
        string? folderPath = null;
        var folderExisted = true;
        var createdFolders = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        bool changed;
        try
        {
            changed = await ExecuteSelfProfileMutationAsync(async () =>
            {
                var expectedFolderPath =
                    FriendFolderLayout.GetSelfFolderPath(configuredRoot);
                EnsureSelfFolderIsAvailable(expectedFolderPath);
                folderExisted = Directory.Exists(expectedFolderPath);
                folderPath = CreateSelfProfileFolder(
                    configuredRoot,
                    cardType == CardType.Character,
                    createdFolders);
                if (string.Equals(
                        GetFolder(SelfProfile, cardType),
                        folderPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                SetFolderPath(SelfProfile, cardType, folderPath);
                await SaveSelfProfileAsync();
                return true;
            });
        }
        catch
        {
            TryRemoveEmptyCreatedFolders(createdFolders);
            throw;
        }

        if (folderPath is null)
            throw new InvalidOperationException("The self profile folder was not created.");

        if (changed || createdFolders.Count > 0)
            NotifySelfProfileChanged(FriendChangeKinds.Folders);
        if (changed || !folderExisted)
            NotifyLibrarySourcesChanged(GetSourceKind(cardType));
        return folderPath;
    }

    public async Task BindExistingSelfProfileFoldersAsync(
        SelfProfileFolderBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var normalizedBindings = new SelfProfileFolderBindings(
            NormalizeExistingSelfFolder(bindings.SceneFolderPath),
            NormalizeExistingSelfFolder(bindings.CharacterFolderPath),
            NormalizeExistingSelfFolder(bindings.CoordinateFolderPath));

        var changedKinds = FriendLibrarySourceKinds.None;
        var changed = await ExecuteSelfProfileMutationAsync(async () =>
        {
            EnsureSelfFolderBindingsAreAvailable(normalizedBindings);
            changedKinds |= BindExistingSelfFolder(
                CardType.Scene,
                normalizedBindings.SceneFolderPath);
            changedKinds |= BindExistingSelfFolder(
                CardType.Character,
                normalizedBindings.CharacterFolderPath);
            changedKinds |= BindExistingSelfFolder(
                CardType.Coordinate,
                normalizedBindings.CoordinateFolderPath);
            if (changedKinds == FriendLibrarySourceKinds.None)
                return false;

            await SaveSelfProfileAsync();
            return true;
        });
        if (!changed)
            return;

        NotifySelfProfileChanged(FriendChangeKinds.Folders);
        NotifyLibrarySourcesChanged(changedKinds);
    }

    public async Task CreateSelfProfileFoldersAsync(FriendFolderRoots roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var targets = GetSelfFolderTargets(roots);

        var createdFolders = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var changedKinds = FriendLibrarySourceKinds.None;
        var sourceKinds = FriendLibrarySourceKinds.None;
        try
        {
            await ExecuteSelfProfileMutationAsync(async () =>
            {
                foreach (var target in targets)
                {
                    EnsureFolderIsAvailable(
                        target.FolderPath,
                        SelfProfile.Id);
                }

                var folderExistence = targets.ToDictionary(
                    target => target.CardType,
                    target => Directory.Exists(target.FolderPath));

                foreach (var target in targets)
                {
                    var folderPath = CreateSelfProfileFolder(
                        target.ConfiguredRoot,
                        target.CardType == CardType.Character,
                        createdFolders);
                    if (!folderExistence[target.CardType])
                        sourceKinds |= GetSourceKind(target.CardType);
                    if (FriendFolderLayout.AreSamePath(
                            GetFolder(SelfProfile, target.CardType) ?? string.Empty,
                            folderPath))
                    {
                        continue;
                    }

                    SetFolderPath(SelfProfile, target.CardType, folderPath);
                    changedKinds |= GetSourceKind(target.CardType);
                }

                if (changedKinds != FriendLibrarySourceKinds.None)
                    await SaveSelfProfileAsync();
            });
        }
        catch
        {
            TryRemoveEmptyCreatedFolders(createdFolders);
            throw;
        }

        if (changedKinds != FriendLibrarySourceKinds.None
            || createdFolders.Count > 0)
        {
            NotifySelfProfileChanged(FriendChangeKinds.Folders);
        }

        sourceKinds |= changedKinds;
        if (sourceKinds != FriendLibrarySourceKinds.None)
            NotifyLibrarySourcesChanged(sourceKinds);
    }

    public async Task ClearFolderAsync(Guid id, CardType cardType)
    {
        var changed = await ExecuteMutationAsync(async () =>
        {
            var friend = GetRequired(id);
            if (GetFolder(friend, cardType) is null)
                return false;

            SetFolderPath(friend, cardType, null);
            await SaveAsync();
            return true;
        });
        if (!changed)
            return;

        NotifyFriendChanged(id, FriendChangeKinds.Folders);
        NotifyLibrarySourcesChanged(GetSourceKind(cardType));
    }

    public async Task ClearSelfProfileFolderAsync(CardType cardType)
    {
        var changed = await ExecuteSelfProfileMutationAsync(async () =>
        {
            if (GetFolder(SelfProfile, cardType) is null)
                return false;

            SetFolderPath(SelfProfile, cardType, null);
            await SaveSelfProfileAsync();
            return true;
        });
        if (!changed)
            return;

        NotifySelfProfileChanged(FriendChangeKinds.Folders);
        NotifyLibrarySourcesChanged(GetSourceKind(cardType));
    }

    public async Task DeleteAsync(Guid id)
    {
        var changedSources = FriendLibrarySourceKinds.None;
        string? avatarPath = null;
        await ExecuteMutationAsync(async () =>
        {
            var friend = GetRequired(id);
            changedSources = GetFolderSourceKinds(friend);
            if (friend.CardPaths.Count > 0)
                changedSources |= FriendLibrarySourceKinds.All;
            avatarPath = friend.AvatarPath;
            Friends.Remove(friend);
            await SaveAsync();
        });

        TryRemoveManagedAvatar(avatarPath, "Friends.Avatar.RemoveDeleted");
        NotifyFriendChanged(id, FriendChangeKinds.Collection);
        if (changedSources != FriendLibrarySourceKinds.None)
            NotifyLibrarySourcesChanged(changedSources);
    }

    public async Task<int> AssignCardsAsync(Guid id, IEnumerable<string> filePaths)
    {
        var result = await ExecuteMutationAsync(async () =>
        {
            var friend = GetRequired(id);
            var existing = new HashSet<string>(
                friend.CardPaths,
                StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var changedKinds = FriendLibrarySourceKinds.None;

            foreach (var path in filePaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath))
                    continue;

                var cardType = GetLinkedCardType(fullPath);
                var folderPath = GetFolder(friend, cardType);
                if (cardType == CardType.NotACard
                    || folderPath is not null
                        && FriendFolderLayout.IsWithin(fullPath, folderPath))
                {
                    continue;
                }

                if (existing.Add(fullPath))
                {
                    friend.CardPaths.Add(fullPath);
                    added++;
                    changedKinds |= GetSourceKind(cardType);
                }
            }

            if (added > 0)
                await SaveAsync();
            return (Added: added, ChangedKinds: changedKinds);
        });
        if (result.Added == 0)
            return 0;

        NotifyFriendChanged(id, FriendChangeKinds.Cards);
        NotifyLibrarySourcesChanged(result.ChangedKinds);
        return result.Added;
    }

    public async Task<int> AssignSelfProfileCardsAsync(
        IEnumerable<string> filePaths)
    {
        var result = await ExecuteSelfProfileMutationAsync(async () =>
        {
            var existing = new HashSet<string>(
                SelfProfile.CardPaths,
                StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var changedKinds = FriendLibrarySourceKinds.None;

            foreach (var path in filePaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath))
                    continue;

                var cardType = GetLinkedCardType(fullPath);
                var folderPath = GetFolder(SelfProfile, cardType);
                if (cardType == CardType.NotACard
                    || folderPath is not null
                        && FriendFolderLayout.IsWithin(fullPath, folderPath))
                {
                    continue;
                }

                if (existing.Add(fullPath))
                {
                    SelfProfile.CardPaths.Add(fullPath);
                    added++;
                    changedKinds |= GetSourceKind(cardType);
                }
            }

            if (added > 0)
                await SaveSelfProfileAsync();
            return (Added: added, ChangedKinds: changedKinds);
        });
        if (result.Added == 0)
            return 0;

        NotifySelfProfileChanged(FriendChangeKinds.Cards);
        NotifyLibrarySourcesChanged(result.ChangedKinds);
        return result.Added;
    }

    public async Task RemoveCardAsync(
        Guid id,
        string filePath)
    {
        var changed = await ExecuteMutationAsync(async () =>
        {
            var friend = GetRequired(id);
            var index = friend.CardPaths.FindIndex(path =>
                path.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;

            friend.CardPaths.RemoveAt(index);
            await SaveAsync();
            return true;
        });
        if (!changed)
            return;

        lock (_linkedCardClassificationLock)
            _linkedCardClassifications.Remove(filePath);
        NotifyFriendChanged(id, FriendChangeKinds.Cards);
        NotifyLibrarySourcesChanged(FriendLibrarySourceKinds.All);
    }

    public async Task RemoveSelfProfileCardAsync(string filePath)
    {
        var changed = await ExecuteSelfProfileMutationAsync(async () =>
        {
            var index = SelfProfile.CardPaths.FindIndex(path =>
                path.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;

            SelfProfile.CardPaths.RemoveAt(index);
            await SaveSelfProfileAsync();
            return true;
        });
        if (!changed)
            return;

        lock (_linkedCardClassificationLock)
            _linkedCardClassifications.Remove(filePath);
        NotifySelfProfileChanged(FriendChangeKinds.Cards);
        NotifyLibrarySourcesChanged(FriendLibrarySourceKinds.All);
    }

    public string EnsureCharacterVariantFolders(Guid id)
    {
        var friend = GetRequired(id);
        if (friend.CharacterFolderPath is null)
            throw new InvalidOperationException("The friend does not have a dedicated folder.");

        var createdFolders = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        try
        {
            FriendFolderLayout.EnsureCharacterVariantFolders(
                friend.CharacterFolderPath,
                path => { createdFolders.Add(path); });
        }
        catch
        {
            TryRemoveEmptyCreatedFolders(createdFolders);
            throw;
        }

        if (createdFolders.Count > 0)
            NotifyFriendChanged(id, FriendChangeKinds.Folders);
        return friend.CharacterFolderPath;
    }

    public string EnsureSelfProfileCharacterVariantFolders()
    {
        if (SelfProfile.CharacterFolderPath is null)
        {
            throw new InvalidOperationException(
                "The self profile does not have a dedicated folder.");
        }

        var createdFolders = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        try
        {
            FriendFolderLayout.EnsureCharacterVariantFolders(
                SelfProfile.CharacterFolderPath,
                path => { createdFolders.Add(path); });
        }
        catch
        {
            TryRemoveEmptyCreatedFolders(createdFolders);
            throw;
        }

        if (createdFolders.Count > 0)
            NotifySelfProfileChanged(FriendChangeKinds.Folders);
        return SelfProfile.CharacterFolderPath;
    }

    public FriendRecord? Find(Guid id) =>
        Friends.FirstOrDefault(friend => friend.Id == id);

    public IEnumerable<string> GetSceneFolders() =>
        GetExistingCategoryFolders(owner => owner.SceneFolderPath);

    public IEnumerable<string> GetCharacterFolders() =>
        GetExistingCategoryFolders(owner => owner.CharacterFolderPath);

    public IEnumerable<string> GetCoordinateFolders() =>
        GetExistingCategoryFolders(owner => owner.CoordinateFolderPath);

    public IReadOnlyList<string> GetLinkedCardPaths() =>
        Friends.SelectMany(friend => friend.CardPaths)
            .Concat(SelfProfile.CardPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Captures the friend-owned library sources with every filesystem check
    /// already done, so the caller can work from memory afterwards.
    ///
    /// The in-memory records are read on the calling thread — <see cref="Friends"/>
    /// is a UI-bound collection and must not be enumerated from a worker — and
    /// only the folder and card checks run in the background.
    /// </summary>
    public Task<LinkedLibrarySnapshot> CaptureLibrarySnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var sceneFolders = GetCategoryFolders(owner => owner.SceneFolderPath);
        var characterFolders = GetCategoryFolders(owner => owner.CharacterFolderPath);
        var coordinateFolders = GetCategoryFolders(owner => owner.CoordinateFolderPath);
        var cardPaths = GetLinkedCardPaths();

        return Task.Run(
            () =>
            {
                var sceneCards = new List<string>();
                var characterCards = new List<string>();
                var coordinateCards = new List<string>();
                foreach (var path in cardPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    switch (GetLinkedCardType(path))
                    {
                        case CardType.Scene:
                            sceneCards.Add(path);
                            break;
                        case CardType.Character:
                            characterCards.Add(path);
                            break;
                        case CardType.Coordinate:
                            coordinateCards.Add(path);
                            break;
                    }
                }

                return new LinkedLibrarySnapshot(
                    KeepExistingDirectories(sceneFolders, cancellationToken),
                    KeepExistingDirectories(characterFolders, cancellationToken),
                    KeepExistingDirectories(coordinateFolders, cancellationToken),
                    sceneCards,
                    characterCards,
                    coordinateCards);
            },
            cancellationToken);
    }

    private static List<string> KeepExistingDirectories(
        IReadOnlyList<string> folders,
        CancellationToken cancellationToken)
    {
        var result = new List<string>(folders.Count);
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(folder))
                result.Add(folder);
        }

        return result;
    }

    public CardType GetLinkedCardType(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return CardType.NotACard;

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
                return CardType.NotACard;

            lock (_linkedCardClassificationLock)
            {
                if (_linkedCardClassifications.TryGetValue(
                        info.FullName,
                        out var cached)
                    && cached.Length == info.Length
                    && cached.LastWriteTimeUtcTicks
                        == info.LastWriteTimeUtc.Ticks)
                {
                    return cached.CardType;
                }
            }

            // Classify outside the lock: opening and parsing the card would
            // otherwise block every other caller on disk I/O. Two threads
            // racing on the same file compute the same answer, and an entry
            // whose stamp no longer matches the file is simply re-classified.
            var cardType = CardTypeClassifier.Classify(info.FullName);
            lock (_linkedCardClassificationLock)
            {
                _linkedCardClassifications[info.FullName] = new(
                    info.Length,
                    info.LastWriteTimeUtc.Ticks,
                    cardType);
            }

            return cardType;
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            return CardType.NotACard;
        }
    }

    public string? GetFolder(CardOwnerRecord owner, CardType cardType) =>
        cardType switch
        {
            CardType.Scene => owner.SceneFolderPath,
            CardType.Character => owner.CharacterFolderPath,
            CardType.Coordinate => owner.CoordinateFolderPath,
            _ => null,
        };

    public CardOwnerRecord? FindFolderOwner(
        string folderPath,
        Guid? excludedOwnerId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        return EnumerateOwners()
            .Where(owner => owner.Id != excludedOwnerId)
            .FirstOrDefault(owner => EnumerateFolderPaths(owner).Any(path =>
                FriendFolderLayout.AreSamePath(path, folderPath)));
    }

    public string? GetPrimaryFolder(CardOwnerRecord owner) =>
        owner.SceneFolderPath
        ?? owner.CharacterFolderPath
        ?? owner.CoordinateFolderPath;

    public string? GetAvatarPath(CardOwnerRecord owner)
    {
        var path = GetLinkedAuthor(owner)?.AvatarPath ?? owner.AvatarPath;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? path
            : null;
    }

    public IReadOnlyList<AuthorDisplay> GetKnownAuthorAvatars() =>
        _authorInfoService.GetSummaries()
            .Select(summary => summary.Display)
            .Where(author =>
                !string.IsNullOrWhiteSpace(author.AvatarPath)
                && File.Exists(author.AvatarPath))
            .DistinctBy(author => author.Key)
            .OrderBy(author => author.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public bool IsAssignedTo(
        CardOwnerRecord owner,
        string filePath,
        CardType cardType)
    {
        if (owner.CardPaths.Any(path =>
                path.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var folderPath = GetFolder(owner, cardType);
        return folderPath is not null
            && FriendFolderLayout.IsWithin(filePath, folderPath);
    }

    public bool IsInsideDedicatedFolder(
        CardOwnerRecord owner,
        string filePath,
        CardType cardType)
    {
        var folderPath = GetFolder(owner, cardType);
        return folderPath is not null
            && FriendFolderLayout.IsWithin(filePath, folderPath);
    }

    /// <summary>
    /// The configured folders for a category, without checking the filesystem.
    /// Reading the owner records is memory-only and safe on the UI thread.
    /// </summary>
    private List<string> GetCategoryFolders(
        Func<CardOwnerRecord, string?> getCategoryFolder)
    {
        var result = new List<string>();
        if (getCategoryFolder(SelfProfile) is { } selfPath)
            result.Add(selfPath);

        foreach (var friend in Friends)
        {
            if (getCategoryFolder(friend) is { } path)
                result.Add(path);
        }

        return result;
    }

    private IEnumerable<string> GetExistingCategoryFolders(
        Func<CardOwnerRecord, string?> getCategoryFolder)
    {
        foreach (var path in GetCategoryFolders(getCategoryFolder))
        {
            if (Directory.Exists(path))
                yield return path;
        }
    }

    private FriendRecord GetRequired(Guid id) =>
        Find(id) ?? throw new InvalidOperationException($"Friend {id} was not found.");

    private async Task ExecuteMutationAsync(Func<Task> mutation)
    {
        await _mutationLock.WaitAsync();
        try
        {
            await FriendMutationTransaction.ExecuteAsync(Friends, mutation);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private async Task<T> ExecuteMutationAsync<T>(Func<Task<T>> mutation)
    {
        await _mutationLock.WaitAsync();
        try
        {
            return await FriendMutationTransaction.ExecuteAsync(
                Friends,
                mutation);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private async Task ExecuteSelfProfileMutationAsync(Func<Task> mutation)
    {
        await _mutationLock.WaitAsync();
        try
        {
            await SelfProfileMutationTransaction.ExecuteAsync(
                SelfProfile,
                mutation);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private async Task<T> ExecuteSelfProfileMutationAsync<T>(
        Func<Task<T>> mutation)
    {
        await _mutationLock.WaitAsync();
        try
        {
            return await SelfProfileMutationTransaction.ExecuteAsync(
                SelfProfile,
                mutation);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            await _store.SaveAsync(Friends);
        }
        catch (Exception ex)
        {
            _logger.LogError("Friends.Save", ex);
            throw;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task SaveSelfProfileAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            await _selfProfileStore.SaveAsync(SelfProfile);
        }
        catch (Exception ex)
        {
            _logger.LogError("SelfProfile.Save", ex);
            throw;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void SortFriends()
    {
        var ordered = Friends
            .OrderBy(friend => friend.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var currentIndex = Friends.IndexOf(ordered[index]);
            if (currentIndex != index)
                Friends.Move(currentIndex, index);
        }
    }

    private string? CreatePersonalFolder(
        string? configuredRoot,
        string friendName,
        Guid ownerId,
        bool createCharacterVariantFolders,
        ISet<string> createdFolders)
    {
        if (string.IsNullOrWhiteSpace(configuredRoot))
            return null;

        var path = FriendFolderLayout.FindExistingPersonalFolderPath(
                configuredRoot,
                friendName)
            ?? FriendFolderLayout.GetPersonalFolderPath(
                configuredRoot,
                friendName);
        EnsureFolderIsAvailable(path, ownerId);
        var existed = Directory.Exists(path);
        path = FriendFolderLayout.CreatePersonalFolder(
            configuredRoot,
            friendName);
        if (!existed)
            createdFolders.Add(path);
        if (createCharacterVariantFolders)
        {
            FriendFolderLayout.EnsureCharacterVariantFolders(
                path,
                createdPath => { createdFolders.Add(createdPath); });
        }
        return path;
    }

    private static string CreateSelfProfileFolder(
        string configuredRoot,
        bool createCharacterVariantFolders,
        ISet<string> createdFolders)
    {
        var path = FriendFolderLayout.GetSelfFolderPath(configuredRoot);
        var existed = Directory.Exists(path);
        path = FriendFolderLayout.CreateSelfFolder(configuredRoot);
        if (!existed)
            createdFolders.Add(path);
        if (createCharacterVariantFolders)
        {
            FriendFolderLayout.EnsureCharacterVariantFolders(
                path,
                createdPath => { createdFolders.Add(createdPath); });
        }

        return path;
    }

    private void EnsureSelfFolderIsAvailable(string selfFolderPath)
    {
        EnsureFolderIsAvailable(selfFolderPath, SelfProfile.Id);
    }

    private void EnsureFolderIsAvailable(
        string folderPath,
        Guid ownerId)
    {
        if (FindFolderOwner(folderPath, ownerId) is not null)
        {
            throw new InvalidOperationException(
                "The folder is already assigned to another card owner.");
        }
    }

    private static string? NormalizeExistingSelfFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return null;

        var fullPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException(fullPath);
        if (!FriendFolderLayout.IsSelfFolderPath(fullPath))
        {
            throw new ArgumentException(
                "The folder must be a #Personal/#Self folder.",
                nameof(folderPath));
        }

        return fullPath;
    }

    private void EnsureSelfFolderBindingsAreAvailable(
        SelfProfileFolderBindings bindings)
    {
        foreach (var path in EnumerateFolderPaths(bindings))
            EnsureFolderIsAvailable(path, SelfProfile.Id);
    }

    private FriendLibrarySourceKinds BindExistingSelfFolder(
        CardType cardType,
        string? folderPath)
    {
        if (folderPath is null
            || GetFolder(SelfProfile, cardType) is { } currentPath
            && FriendFolderLayout.AreSamePath(currentPath, folderPath))
        {
            return FriendLibrarySourceKinds.None;
        }

        SetFolderPath(SelfProfile, cardType, folderPath);
        return GetSourceKind(cardType);
    }

    private static IReadOnlyList<SelfFolderTarget> GetSelfFolderTargets(
        FriendFolderRoots roots)
    {
        var targets = new List<SelfFolderTarget>(3);
        AddTarget(CardType.Scene, roots.SceneRootPath);
        AddTarget(CardType.Character, roots.CharacterRootPath);
        AddTarget(CardType.Coordinate, roots.CoordinateRootPath);
        return targets;

        void AddTarget(CardType cardType, string? configuredRoot)
        {
            if (string.IsNullOrWhiteSpace(configuredRoot))
                return;

            var fullRoot = Path.GetFullPath(configuredRoot);
            var folderPath = FriendFolderLayout.FindExistingSelfFolderPath(
                    fullRoot)
                ?? FriendFolderLayout.GetSelfFolderPath(fullRoot);
            targets.Add(new SelfFolderTarget(
                cardType,
                fullRoot,
                folderPath));
        }
    }

    private IEnumerable<CardOwnerRecord> EnumerateOwners()
    {
        yield return SelfProfile;
        foreach (var friend in Friends)
            yield return friend;
    }

    private static IEnumerable<string> EnumerateFolderPaths(
        CardOwnerRecord owner)
    {
        if (!string.IsNullOrWhiteSpace(owner.SceneFolderPath))
            yield return owner.SceneFolderPath;
        if (!string.IsNullOrWhiteSpace(owner.CharacterFolderPath))
            yield return owner.CharacterFolderPath;
        if (!string.IsNullOrWhiteSpace(owner.CoordinateFolderPath))
            yield return owner.CoordinateFolderPath;
    }

    private static IEnumerable<string> EnumerateFolderPaths(
        SelfProfileFolderBindings bindings)
    {
        if (!string.IsNullOrWhiteSpace(bindings.SceneFolderPath))
            yield return bindings.SceneFolderPath;
        if (!string.IsNullOrWhiteSpace(bindings.CharacterFolderPath))
            yield return bindings.CharacterFolderPath;
        if (!string.IsNullOrWhiteSpace(bindings.CoordinateFolderPath))
            yield return bindings.CoordinateFolderPath;
    }

    private static FriendLibrarySourceKinds GetSourceKind(CardType cardType) =>
        cardType switch
        {
            CardType.Scene => FriendLibrarySourceKinds.Scene,
            CardType.Character => FriendLibrarySourceKinds.Character,
            CardType.Coordinate => FriendLibrarySourceKinds.Coordinate,
            _ => FriendLibrarySourceKinds.None,
        };

    private static FriendLibrarySourceKinds GetFolderSourceKinds(
        CardOwnerRecord owner)
    {
        var kinds = FriendLibrarySourceKinds.None;
        if (!string.IsNullOrWhiteSpace(owner.SceneFolderPath))
            kinds |= FriendLibrarySourceKinds.Scene;
        if (!string.IsNullOrWhiteSpace(owner.CharacterFolderPath))
            kinds |= FriendLibrarySourceKinds.Character;
        if (!string.IsNullOrWhiteSpace(owner.CoordinateFolderPath))
            kinds |= FriendLibrarySourceKinds.Coordinate;
        return kinds;
    }

    private static void SetFolderPath(
        CardOwnerRecord owner,
        CardType cardType,
        string? folderPath)
    {
        switch (cardType)
        {
            case CardType.Scene:
                owner.SceneFolderPath = folderPath;
                break;
            case CardType.Character:
                owner.CharacterFolderPath = folderPath;
                break;
            case CardType.Coordinate:
                owner.CoordinateFolderPath = folderPath;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(cardType),
                    cardType,
                    "A supported card type is required.");
        }
    }

    private AuthorDisplay? GetLinkedAuthor(CardOwnerRecord owner)
    {
        if (string.IsNullOrWhiteSpace(owner.AvatarAuthorProviderId)
            || string.IsNullOrWhiteSpace(owner.AvatarAuthorId))
        {
            return null;
        }

        return _authorInfoService.FindDisplay(
            owner.AvatarAuthorProviderId,
            owner.AvatarAuthorId);
    }

    private void OnAuthorProfileChanged(AuthorKey key)
    {
        foreach (var friend in Friends)
        {
            if (string.Equals(
                    friend.AvatarAuthorProviderId,
                    key.ProviderId,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    friend.AvatarAuthorId,
                    key.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                NotifyFriendChanged(friend.Id, FriendChangeKinds.Avatar);
            }
        }

        if (string.Equals(
                SelfProfile.AvatarAuthorProviderId,
                key.ProviderId,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                SelfProfile.AvatarAuthorId,
                key.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            NotifySelfProfileChanged(FriendChangeKinds.Avatar);
        }
    }

    private void NotifyFriendChanged(
        Guid friendId,
        FriendChangeKinds kinds)
    {
        var handlers = FriendChanged;
        if (handlers is null)
            return;

        var change = new FriendChange(friendId, kinds);
        foreach (Action<FriendChange> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(change);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Friends.NotifyFriendChanged",
                    ex,
                    friendId.ToString());
            }
        }
    }

    private void NotifySelfProfileChanged(FriendChangeKinds kinds)
    {
        var handlers = SelfProfileChanged;
        if (handlers is null)
            return;

        foreach (Action<FriendChangeKinds> handler
                 in handlers.GetInvocationList())
        {
            try
            {
                handler(kinds);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "SelfProfile.NotifyChanged",
                    ex,
                    kinds.ToString());
            }
        }
    }

    private void NotifyLibrarySourcesChanged(FriendLibrarySourceKinds kinds)
    {
        if (kinds == FriendLibrarySourceKinds.None)
            return;

        var handlers = LibrarySourcesChanged;
        if (handlers is null)
            return;

        foreach (Action<FriendLibrarySourceKinds> handler
                 in handlers.GetInvocationList())
        {
            try
            {
                handler(kinds);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Friends.NotifyLibrarySourcesChanged",
                    ex,
                    kinds.ToString());
            }
        }
    }

    private void TryRemoveManagedAvatar(string? path, string operation)
    {
        try
        {
            _avatarStorage.RemoveIfManaged(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(operation, ex, path);
        }
    }

    private void TryRemoveEmptyCreatedFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)
                && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Friends.Folder.RollbackCreated", ex, path);
        }
    }

    private void TryRemoveEmptyCreatedFolders(IEnumerable<string> paths)
    {
        foreach (var path in paths.OrderByDescending(path => path.Length))
            TryRemoveEmptyCreatedFolder(path);
    }

}

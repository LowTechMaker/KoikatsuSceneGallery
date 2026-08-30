using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class FriendFeatureTests
{
    [Fact]
    public async Task FriendStore_MissingFileLoadsEmptyCollection()
    {
        using var directory = new TestDirectory();
        var store = new FriendStore(Path.Combine(directory.Path, "friends.json"));

        var friends = await store.LoadAsync();

        Assert.Empty(friends);
    }

    [Fact]
    public async Task FriendStore_RoundTripsFolderAndCardLinks()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "friends.json");
        var store = new FriendStore(path);
        var id = Guid.NewGuid();
        var records = new[]
        {
            new FriendRecord
            {
                Id = id,
                Name = "小明",
                SceneFolderPath = Path.Combine(directory.Path, "Scenes", "小明"),
                CharacterFolderPath = Path.Combine(directory.Path, "Characters", "小明"),
                CoordinateFolderPath = Path.Combine(directory.Path, "Coordinates", "小明"),
                AvatarPath = Path.Combine(directory.Path, "avatars", "小明.png"),
                AvatarAuthorProviderId = "pixiv",
                AvatarAuthorId = "12345",
                CardPaths = [Path.Combine(directory.Path, "card.png")],
            },
        };

        await store.SaveAsync(records);
        var loaded = await store.LoadAsync();

        var friend = Assert.Single(loaded);
        Assert.Equal(id, friend.Id);
        Assert.Equal("小明", friend.Name);
        Assert.Equal(records[0].SceneFolderPath, friend.SceneFolderPath);
        Assert.Equal(records[0].CharacterFolderPath, friend.CharacterFolderPath);
        Assert.Equal(records[0].CoordinateFolderPath, friend.CoordinateFolderPath);
        Assert.Equal(records[0].AvatarPath, friend.AvatarPath);
        Assert.Equal(records[0].AvatarAuthorProviderId, friend.AvatarAuthorProviderId);
        Assert.Equal(records[0].AvatarAuthorId, friend.AvatarAuthorId);
        Assert.Equal(records[0].CardPaths, friend.CardPaths);
    }

    [Fact]
    public void FriendRecordRepair_InvalidFieldsDoNotDiscardUsableData()
    {
        using var directory = new TestDirectory();
        var validCardPath = Path.Combine(directory.Path, "card.png");
        var invalidPath = $"broken{'\0'}path";
        var friend = new FriendRecord
        {
            Id = Guid.Empty,
            Name = "  ",
            FolderPath = invalidPath,
            SceneFolderPath = directory.Path,
            CardPaths = [validCardPath, invalidPath, validCardPath],
        };
        var invalidPaths = new List<string>();

        var changed = FriendRecordRepair.Repair(
            friend,
            new HashSet<Guid>(),
            (path, _) => invalidPaths.Add(path));

        Assert.True(changed);
        Assert.NotEqual(Guid.Empty, friend.Id);
        Assert.False(string.IsNullOrWhiteSpace(friend.Name));
        Assert.Null(friend.FolderPath);
        Assert.Equal(Path.GetFullPath(directory.Path), friend.SceneFolderPath);
        Assert.Equal([Path.GetFullPath(validCardPath)], friend.CardPaths);
        Assert.Contains(invalidPath, invalidPaths);
    }

    [Fact]
    public void FriendRecordRepair_ReplacesDuplicateIds()
    {
        var duplicateId = Guid.NewGuid();
        var usedIds = new HashSet<Guid> { duplicateId };
        var friend = new FriendRecord
        {
            Id = duplicateId,
            Name = "朋友",
        };

        var changed = FriendRecordRepair.Repair(friend, usedIds);

        Assert.True(changed);
        Assert.NotEqual(duplicateId, friend.Id);
        Assert.Contains(friend.Id, usedIds);
    }

    [Fact]
    public async Task FriendAvatarStorage_ImportsImageIntoManagedFolder()
    {
        using var directory = new TestDirectory();
        var sourcePath = Path.Combine(directory.Path, "source.png");
        await File.WriteAllBytesAsync(sourcePath, TestFiles.Png(10, 20));
        var avatarFolder = Path.Combine(directory.Path, "avatars");
        var storage = new FriendAvatarStorage(avatarFolder);

        var importedPath = await storage.ImportAsync(Guid.NewGuid(), sourcePath);

        Assert.True(File.Exists(importedPath));
        Assert.True(FriendFolderLayout.IsWithin(importedPath, avatarFolder));
        Assert.Equal(
            await File.ReadAllBytesAsync(sourcePath),
            await File.ReadAllBytesAsync(importedPath));
    }

    [Fact]
    public async Task FriendAvatarStorage_RejectsUnsupportedImageType()
    {
        using var directory = new TestDirectory();
        var sourcePath = Path.Combine(directory.Path, "source.txt");
        await File.WriteAllTextAsync(sourcePath, "not an image");
        var storage = new FriendAvatarStorage(
            Path.Combine(directory.Path, "avatars"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.ImportAsync(Guid.NewGuid(), sourcePath));
    }

    [Fact]
    public async Task FriendAvatarStorage_RejectsInvalidImageContent()
    {
        using var directory = new TestDirectory();
        var sourcePath = Path.Combine(directory.Path, "source.png");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
        var storage = new FriendAvatarStorage(
            Path.Combine(directory.Path, "avatars"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.ImportAsync(Guid.NewGuid(), sourcePath));
    }

    [Fact]
    public async Task FriendAvatarStorage_OnlyRemovesManagedImages()
    {
        using var directory = new TestDirectory();
        var sourcePath = Path.Combine(directory.Path, "source.png");
        await File.WriteAllBytesAsync(sourcePath, TestFiles.Png(10, 20));
        var avatarFolder = Path.Combine(directory.Path, "avatars");
        var storage = new FriendAvatarStorage(avatarFolder);
        var importedPath = await storage.ImportAsync(Guid.NewGuid(), sourcePath);

        storage.RemoveIfManaged(sourcePath);
        storage.RemoveIfManaged(importedPath);

        Assert.True(File.Exists(sourcePath));
        Assert.False(File.Exists(importedPath));
    }

    [Fact]
    public void CreatePersonalFolder_UsesConfiguredRootAndReservedFolder()
    {
        using var directory = new TestDirectory();

        var friendFolder = FriendFolderLayout.CreatePersonalFolder(
            directory.Path,
            "朋友");

        Assert.Equal(
            Path.Combine(
                directory.Path,
                FriendFolderLayout.PersonalFolderName,
                "朋友"),
            friendFolder);
        Assert.True(Directory.Exists(friendFolder));
    }

    [Fact]
    public void GetPersonalFolderPath_DoesNotCreateDirectory()
    {
        using var directory = new TestDirectory();

        var friendFolder = FriendFolderLayout.GetPersonalFolderPath(
            directory.Path,
            "朋友");

        Assert.Equal(
            Path.Combine(
                directory.Path,
                FriendFolderLayout.PersonalFolderName,
                "朋友"),
            friendFolder);
        Assert.False(Directory.Exists(friendFolder));
    }

    [Fact]
    public void FindExistingPersonalFolderPath_MatchesPhysicalNameIgnoringCase()
    {
        using var directory = new TestDirectory();
        var personalFolder = Directory.CreateDirectory(Path.Combine(
            directory.Path,
            "#personal"));
        var existingFolder = Directory.CreateDirectory(Path.Combine(
            personalFolder.FullName,
            "ALICE"));

        var result = FriendFolderLayout.FindExistingPersonalFolderPath(
            directory.Path,
            "alice");

        Assert.Equal(existingFolder.FullName, result);
    }

    [Fact]
    public void CreatePersonalFolder_ReusesCaseInsensitiveExistingFolder()
    {
        using var directory = new TestDirectory();
        var personalFolder = Directory.CreateDirectory(Path.Combine(
            directory.Path,
            FriendFolderLayout.PersonalFolderName));
        var existingFolder = Directory.CreateDirectory(Path.Combine(
            personalFolder.FullName,
            "FRIEND"));

        var result = FriendFolderLayout.CreatePersonalFolder(
            directory.Path,
            "friend");

        Assert.Equal(existingFolder.FullName, result);
        Assert.Single(Directory.EnumerateDirectories(personalFolder.FullName));
    }

    [Fact]
    public void FindExistingPersonalFolderPath_UsesSanitizedPhysicalName()
    {
        using var directory = new TestDirectory();
        var personalFolder = Directory.CreateDirectory(Path.Combine(
            directory.Path,
            FriendFolderLayout.PersonalFolderName));
        var safeName = Path.GetInvalidFileNameChars().Contains(':')
            ? "FRIEND_NAME"
            : "FRIEND:NAME";
        var existingFolder = Directory.CreateDirectory(Path.Combine(
            personalFolder.FullName,
            safeName));

        var result = FriendFolderLayout.FindExistingPersonalFolderPath(
            directory.Path,
            "friend:name");

        Assert.Equal(existingFolder.FullName, result);
    }

    [Fact]
    public void FindExistingSelfFolderPath_MatchesReservedNamesIgnoringCase()
    {
        using var directory = new TestDirectory();
        var personalFolder = Directory.CreateDirectory(Path.Combine(
            directory.Path,
            "#personal"));
        var existingFolder = Directory.CreateDirectory(Path.Combine(
            personalFolder.FullName,
            "#self"));

        var result = FriendFolderLayout.FindExistingSelfFolderPath(
            directory.Path);

        Assert.Equal(existingFolder.FullName, result);
        Assert.True(FriendFolderLayout.IsSelfFolderPath(result!));
    }

    [Fact]
    public void IsSelfFolderPath_RejectsSameNameOutsidePersonalFolder()
    {
        using var directory = new TestDirectory();
        var unrelatedFolder = Directory.CreateDirectory(Path.Combine(
            directory.Path,
            FriendFolderLayout.SelfFolderName));

        Assert.False(FriendFolderLayout.IsSelfFolderPath(
            unrelatedFolder.FullName));
    }

    [Fact]
    public void CollapseNestedRoots_RemovesCoveredChildren()
    {
        using var directory = new TestDirectory();
        var root = Path.Combine(directory.Path, "cards");
        var child = Path.Combine(root, FriendFolderLayout.PersonalFolderName, "朋友");
        var independent = Path.Combine(directory.Path, "other");

        var result = FriendFolderLayout.CollapseNestedRoots(
            [child, independent, root, root]);

        Assert.Equal(2, result.Count);
        Assert.Contains(Path.GetFullPath(root), result);
        Assert.Contains(Path.GetFullPath(independent), result);
        Assert.DoesNotContain(Path.GetFullPath(child), result);
    }

    [Fact]
    public void FriendSourceSet_IgnoresNestedRootsAndCoveredLinks()
    {
        using var directory = new TestDirectory();
        var root = Path.Combine(directory.Path, "cards");
        var friendRoot = Path.Combine(
            root,
            FriendFolderLayout.PersonalFolderName,
            "朋友");
        var coveredCard = Path.Combine(friendRoot, "covered.png");

        var result = FriendSourceSet.Build(
            [root, friendRoot],
            [coveredCard]);

        Assert.Single(result);
        Assert.Contains(
            result,
            source => source.EndsWith(
                Path.GetFullPath(root),
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FriendSourceSet_IncludesExternalLinkedFile()
    {
        using var directory = new TestDirectory();
        var root = Path.Combine(directory.Path, "cards");
        var externalCard = Path.Combine(directory.Path, "external.png");

        var result = FriendSourceSet.Build([root], [externalCard]);

        Assert.Equal(2, result.Count);
        Assert.Contains(
            result,
            source => source.EndsWith(
                Path.GetFullPath(externalCard),
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IsWithin_DoesNotMatchSiblingWithSamePrefix()
    {
        using var directory = new TestDirectory();
        var root = Path.Combine(directory.Path, "cards");
        var siblingCard = Path.Combine(
            directory.Path,
            "cards-old",
            "card.png");

        Assert.False(FriendFolderLayout.IsWithin(siblingCard, root));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void CreatePersonalFolder_RejectsTraversalNames(string friendName)
    {
        using var directory = new TestDirectory();

        Assert.Throws<ArgumentException>(() =>
            FriendFolderLayout.CreatePersonalFolder(
                directory.Path,
                friendName));
    }

    [Fact]
    public void EnsureCharacterVariantFolders_CreatesAtFriendRoot()
    {
        using var directory = new TestDirectory();
        var friendFolder = FriendFolderLayout.CreatePersonalFolder(
            directory.Path,
            "朋友");
        var created = new List<string>();

        FriendFolderLayout.EnsureCharacterVariantFolders(
            friendFolder,
            created.Add);

        Assert.Equal(2, created.Count);
        Assert.True(Directory.Exists(Path.Combine(
            friendFolder,
            FriendFolderLayout.OldVersionFolderName)));
        Assert.True(Directory.Exists(Path.Combine(
            friendFolder,
            FriendFolderLayout.AlternativesFolderName)));
        Assert.True(FriendFolderLayout.HasCharacterVariantFolders(
            friendFolder));

        created.Clear();
        FriendFolderLayout.EnsureCharacterVariantFolders(
            friendFolder,
            created.Add);
        Assert.Empty(created);
    }

    [Fact]
    public void HasCharacterVariantFolders_RequiresBothFolders()
    {
        using var directory = new TestDirectory();
        var friendFolder = FriendFolderLayout.CreatePersonalFolder(
            directory.Path,
            "朋友");
        Directory.CreateDirectory(Path.Combine(
            friendFolder,
            FriendFolderLayout.OldVersionFolderName));

        Assert.False(FriendFolderLayout.HasCharacterVariantFolders(
            friendFolder));
    }

    [Theory]
    [InlineData("#old_version", CharacterVariantKind.OldVersion)]
    [InlineData("#OLD_VERSION", CharacterVariantKind.OldVersion)]
    [InlineData("#alternatives", CharacterVariantKind.Alternative)]
    [InlineData("normal", CharacterVariantKind.Current)]
    public void ClassifyCharacterPath_UsesReservedFolder(
        string folderName,
        CharacterVariantKind expected)
    {
        var path = Path.Combine(
            "C:\\cards",
            FriendFolderLayout.PersonalFolderName,
            "朋友",
            folderName,
            "角色A",
            "card.png");

        Assert.Equal(expected, FriendFolderLayout.ClassifyCharacterPath(path));
    }

    [Fact]
    public void ClassifyCharacterPath_IgnoresReservedFolderOutsideStructure()
    {
        var path = Path.Combine(
            "C:\\cards",
            FriendFolderLayout.OldVersionFolderName,
            "card.png");

        Assert.Equal(
            CharacterVariantKind.Current,
            FriendFolderLayout.ClassifyCharacterPath(path));
    }

    [Fact]
    public void ClassifyCharacterPath_IgnoresPerCharacterReservedFolder()
    {
        var path = Path.Combine(
            "C:\\cards",
            FriendFolderLayout.PersonalFolderName,
            "朋友",
            "角色A",
            FriendFolderLayout.OldVersionFolderName,
            "card.png");

        Assert.Equal(
            CharacterVariantKind.Current,
            FriendFolderLayout.ClassifyCharacterPath(path));
    }

    [Fact]
    public void TryGetFriendCharacterFolder_RecognizesPersonalLayout()
    {
        var friendFolder = Path.Combine(
            "C:\\cards",
            FriendFolderLayout.PersonalFolderName,
            "朋友");
        var alternative = Path.Combine(
            friendFolder,
            FriendFolderLayout.AlternativesFolderName,
            "角色A",
            "alternative.png");

        Assert.Equal(
            Path.GetFullPath(friendFolder),
            FriendFolderLayout.TryGetFriendCharacterFolder(alternative));
    }

    [Fact]
    public void TryGetFriendCharacterFolder_RecognizesLegacyFriendLayout()
    {
        var friendCharacterFolder = Path.Combine(
            "C:\\cards",
            FriendFolderLayout.PersonalFolderName,
            "朋友",
            FriendFolderLayout.CharactersFolderName);
        var oldVersion = Path.Combine(
            friendCharacterFolder,
            FriendFolderLayout.OldVersionFolderName,
            "old.png");

        Assert.Equal(
            Path.GetFullPath(friendCharacterFolder),
            FriendFolderLayout.TryGetFriendCharacterFolder(oldVersion));
    }

    [Fact]
    public void TryGetFriendCharacterFolder_IgnoresCharactersFolderOutsidePersonal()
    {
        var libraryRoot = Path.Combine(
            "C:\\Koikatsu",
            FriendFolderLayout.CharactersFolderName);

        Assert.Null(FriendFolderLayout.TryGetFriendCharacterFolder(
            Path.Combine(libraryRoot, "角色A.png")));
        Assert.Null(FriendFolderLayout.TryGetFriendCharacterFolder(
            Path.Combine(
                libraryRoot,
                FriendFolderLayout.OldVersionFolderName,
                "old.png")));
    }

    [Fact]
    public void TryGetFriendCharacterFolder_UsesPersonalFolderUnderCharactersRoot()
    {
        var friendFolder = Path.Combine(
            "C:\\Koikatsu",
            FriendFolderLayout.CharactersFolderName,
            FriendFolderLayout.PersonalFolderName,
            "朋友");

        Assert.Equal(
            Path.GetFullPath(friendFolder),
            FriendFolderLayout.TryGetFriendCharacterFolder(
                Path.Combine(friendFolder, "角色A.png")));
    }

    [Fact]
    public void ClassifyCharacterPath_IgnoresCharactersRootOutsidePersonal()
    {
        var path = Path.Combine(
            "C:\\Koikatsu",
            FriendFolderLayout.CharactersFolderName,
            FriendFolderLayout.OldVersionFolderName,
            "old.png");

        Assert.Equal(
            CharacterVariantKind.Current,
            FriendFolderLayout.ClassifyCharacterPath(path));
    }

    [Fact]
    public void BuildCharacterVersionKey_SeparatesCharactersWithinFriend()
    {
        var friendFolder = Path.Combine(
            "C:\\cards",
            FriendFolderLayout.PersonalFolderName,
            "朋友");

        var first = FriendFolderLayout.BuildCharacterVersionKey(
            friendFolder,
            "角色甲");
        var second = FriendFolderLayout.BuildCharacterVersionKey(
            friendFolder,
            "角色乙");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BuildCharacterVersionKey_SeparatesSameNameAcrossFriends()
    {
        var first = FriendFolderLayout.BuildCharacterVersionKey(
            Path.Combine("C:\\cards", "#Personal", "朋友甲"),
            "同名角色");
        var second = FriendFolderLayout.BuildCharacterVersionKey(
            Path.Combine("C:\\cards", "#Personal", "朋友乙"),
            "同名角色");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task FriendMutationTransaction_FailureRestoresObjectsAndOrder()
    {
        var first = new FriendRecord
        {
            Name = "甲",
            SceneFolderPath = "scene-a",
            AvatarAuthorProviderId = "pixiv",
            AvatarAuthorId = "1",
            CardPaths = ["a.png"],
        };
        var second = new FriendRecord
        {
            Name = "乙",
            CardPaths = ["b.png"],
        };
        var friends = new System.Collections.ObjectModel
            .ObservableCollection<FriendRecord>([first, second]);

        await Assert.ThrowsAsync<IOException>(() =>
            FriendMutationTransaction.ExecuteAsync(friends, () =>
            {
                first.Name = "最後";
                first.SceneFolderPath = null;
                first.AvatarAuthorProviderId = null;
                first.AvatarAuthorId = null;
                first.CardPaths.Add("new.png");
                friends.Remove(first);
                friends.Add(new FriendRecord { Name = "新增" });
                return Task.FromException(
                    new IOException("Injected save failure."));
            }));

        Assert.Collection(
            friends,
            friend => Assert.Same(first, friend),
            friend => Assert.Same(second, friend));
        Assert.Equal("甲", first.Name);
        Assert.Equal("scene-a", first.SceneFolderPath);
        Assert.Equal("pixiv", first.AvatarAuthorProviderId);
        Assert.Equal("1", first.AvatarAuthorId);
        Assert.Equal(["a.png"], first.CardPaths);
        Assert.Equal(["b.png"], second.CardPaths);
    }

    [Fact]
    public async Task FriendMutationTransaction_SuccessKeepsMutation()
    {
        var friend = new FriendRecord { Name = "原名" };
        var friends = new System.Collections.ObjectModel
            .ObservableCollection<FriendRecord>([friend]);

        await FriendMutationTransaction.ExecuteAsync(friends, () =>
        {
            friend.Name = "新名";
            return Task.CompletedTask;
        });

        Assert.Same(friend, Assert.Single(friends));
        Assert.Equal("新名", friend.Name);
    }

    [Fact]
    public async Task SelfProfileStore_RoundTripsIndependentProfile()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "self-profile.json");
        var store = new SelfProfileStore(path);
        var profile = new SelfProfile
        {
            Name = "我的名稱",
            SceneFolderPath = Path.Combine(directory.Path, "Scenes"),
            CharacterFolderPath = Path.Combine(directory.Path, "Characters"),
            CoordinateFolderPath = Path.Combine(directory.Path, "Coordinates"),
            AvatarAuthorProviderId = "pixiv",
            AvatarAuthorId = "12345",
            CardPaths = [Path.Combine(directory.Path, "card.png")],
        };

        await store.SaveAsync(profile);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(profile.Id, loaded.Id);
        Assert.Equal(profile.Name, loaded.Name);
        Assert.Equal(profile.SceneFolderPath, loaded.SceneFolderPath);
        Assert.Equal(profile.CharacterFolderPath, loaded.CharacterFolderPath);
        Assert.Equal(profile.CoordinateFolderPath, loaded.CoordinateFolderPath);
        Assert.Equal(profile.AvatarAuthorProviderId, loaded.AvatarAuthorProviderId);
        Assert.Equal(profile.AvatarAuthorId, loaded.AvatarAuthorId);
        Assert.Equal(profile.CardPaths, loaded.CardPaths);
    }

    [Fact]
    public void SelfProfileRepair_RepairsInvalidFieldsWithoutRemovingProfile()
    {
        using var directory = new TestDirectory();
        var validCardPath = Path.Combine(directory.Path, "card.png");
        var invalidPath = $"broken{'\0'}path";
        var profile = new SelfProfile
        {
            Id = Guid.Empty,
            Name = "  ",
            SceneFolderPath = directory.Path,
            AvatarPath = invalidPath,
            CardPaths = [validCardPath, invalidPath, validCardPath],
        };
        var invalidPaths = new List<string>();

        var changed = SelfProfileRepair.Repair(
            profile,
            "我",
            (path, _) => invalidPaths.Add(path));

        Assert.True(changed);
        Assert.NotEqual(Guid.Empty, profile.Id);
        Assert.Equal("我", profile.Name);
        Assert.Equal(Path.GetFullPath(directory.Path), profile.SceneFolderPath);
        Assert.Null(profile.AvatarPath);
        Assert.Equal([Path.GetFullPath(validCardPath)], profile.CardPaths);
        Assert.Contains(invalidPath, invalidPaths);
    }

    [Fact]
    public async Task SelfProfileMutationTransaction_FailureRestoresSameObject()
    {
        var profile = new SelfProfile
        {
            Name = "我",
            CharacterFolderPath = "characters",
            CardPaths = ["card.png"],
        };

        await Assert.ThrowsAsync<IOException>(() =>
            SelfProfileMutationTransaction.ExecuteAsync(profile, () =>
            {
                profile.Name = "未儲存名稱";
                profile.CharacterFolderPath = null;
                profile.CardPaths.Add("new.png");
                return Task.FromException(
                    new IOException("Injected save failure."));
            }));

        Assert.Equal("我", profile.Name);
        Assert.Equal("characters", profile.CharacterFolderPath);
        Assert.Equal(["card.png"], profile.CardPaths);
    }

    [Fact]
    public void CreateSelfFolder_UsesStableReservedName()
    {
        using var directory = new TestDirectory();

        var selfFolder = FriendFolderLayout.CreateSelfFolder(directory.Path);

        Assert.Equal(
            Path.Combine(
                directory.Path,
                FriendFolderLayout.PersonalFolderName,
                FriendFolderLayout.SelfFolderName),
            selfFolder);
        Assert.True(Directory.Exists(selfFolder));
    }

    [Fact]
    public void GetPersonalFolderPath_RejectsReservedSelfName()
    {
        using var directory = new TestDirectory();

        Assert.Throws<ArgumentException>(() =>
            FriendFolderLayout.GetPersonalFolderPath(
                directory.Path,
                FriendFolderLayout.SelfFolderName));
    }

    [Fact]
    public void AreSamePath_IgnoresTrailingDirectorySeparator()
    {
        using var directory = new TestDirectory();
        var selfFolder = FriendFolderLayout.GetSelfFolderPath(directory.Path);

        Assert.True(FriendFolderLayout.AreSamePath(
            selfFolder,
            selfFolder + Path.DirectorySeparatorChar));
        Assert.False(FriendFolderLayout.AreSamePath(
            selfFolder,
            Path.Combine(directory.Path, "another")));
    }

}

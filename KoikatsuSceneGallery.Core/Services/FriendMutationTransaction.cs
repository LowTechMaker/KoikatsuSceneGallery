using System.Collections.ObjectModel;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

internal static class FriendMutationTransaction
{
    public static async Task ExecuteAsync(
        ObservableCollection<FriendRecord> friends,
        Func<Task> mutation)
    {
        await ExecuteAsync(friends, async () =>
        {
            await mutation();
            return true;
        });
    }

    public static async Task<T> ExecuteAsync<T>(
        ObservableCollection<FriendRecord> friends,
        Func<Task<T>> mutation)
    {
        ArgumentNullException.ThrowIfNull(friends);
        ArgumentNullException.ThrowIfNull(mutation);

        var snapshot = friends
            .Select(friend => new FriendState(friend, Clone(friend)))
            .ToList();
        try
        {
            return await mutation();
        }
        catch
        {
            foreach (var state in snapshot)
                Copy(state.Snapshot, state.Friend);

            friends.Clear();
            foreach (var state in snapshot)
                friends.Add(state.Friend);
            throw;
        }
    }

    private static FriendRecord Clone(FriendRecord friend) =>
        new()
        {
            Id = friend.Id,
            Name = friend.Name,
            FolderPath = friend.FolderPath,
            SceneFolderPath = friend.SceneFolderPath,
            CharacterFolderPath = friend.CharacterFolderPath,
            CoordinateFolderPath = friend.CoordinateFolderPath,
            AvatarPath = friend.AvatarPath,
            AvatarAuthorProviderId = friend.AvatarAuthorProviderId,
            AvatarAuthorId = friend.AvatarAuthorId,
            CardPaths = [.. friend.CardPaths],
        };

    private static void Copy(FriendRecord source, FriendRecord target)
    {
        target.Id = source.Id;
        target.Name = source.Name;
        target.FolderPath = source.FolderPath;
        target.SceneFolderPath = source.SceneFolderPath;
        target.CharacterFolderPath = source.CharacterFolderPath;
        target.CoordinateFolderPath = source.CoordinateFolderPath;
        target.AvatarPath = source.AvatarPath;
        target.AvatarAuthorProviderId = source.AvatarAuthorProviderId;
        target.AvatarAuthorId = source.AvatarAuthorId;
        target.CardPaths = [.. source.CardPaths];
    }

    private sealed record FriendState(
        FriendRecord Friend,
        FriendRecord Snapshot);
}

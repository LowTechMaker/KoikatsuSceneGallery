using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

internal static class SelfProfileMutationTransaction
{
    public static async Task ExecuteAsync(
        SelfProfile profile,
        Func<Task> mutation)
    {
        await ExecuteAsync(profile, async () =>
        {
            await mutation();
            return true;
        });
    }

    public static async Task<T> ExecuteAsync<T>(
        SelfProfile profile,
        Func<Task<T>> mutation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(mutation);

        var snapshot = Clone(profile);
        try
        {
            return await mutation();
        }
        catch
        {
            Copy(snapshot, profile);
            throw;
        }
    }

    private static SelfProfile Clone(SelfProfile profile) =>
        new()
        {
            Id = profile.Id,
            Name = profile.Name,
            SceneFolderPath = profile.SceneFolderPath,
            CharacterFolderPath = profile.CharacterFolderPath,
            CoordinateFolderPath = profile.CoordinateFolderPath,
            AvatarPath = profile.AvatarPath,
            AvatarAuthorProviderId = profile.AvatarAuthorProviderId,
            AvatarAuthorId = profile.AvatarAuthorId,
            CardPaths = [.. profile.CardPaths],
        };

    private static void Copy(SelfProfile source, SelfProfile target)
    {
        target.Id = source.Id;
        target.Name = source.Name;
        target.SceneFolderPath = source.SceneFolderPath;
        target.CharacterFolderPath = source.CharacterFolderPath;
        target.CoordinateFolderPath = source.CoordinateFolderPath;
        target.AvatarPath = source.AvatarPath;
        target.AvatarAuthorProviderId = source.AvatarAuthorProviderId;
        target.AvatarAuthorId = source.AvatarAuthorId;
        target.CardPaths = [.. source.CardPaths];
    }
}

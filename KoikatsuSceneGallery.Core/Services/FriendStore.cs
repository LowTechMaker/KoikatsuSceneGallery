using System.Text.Json;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

internal sealed class FriendStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public async Task<List<FriendRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return [];

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<List<FriendRecord>>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    public async Task SaveAsync(
        IReadOnlyCollection<FriendRecord> friends,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                friends,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, filePath, overwrite: true);
    }
}

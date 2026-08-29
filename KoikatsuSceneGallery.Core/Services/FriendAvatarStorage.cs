namespace KoikatsuSceneGallery.Services;

internal sealed class FriendAvatarStorage(string folderPath)
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".bmp",
            ".jpeg",
            ".jpg",
            ".png",
            ".webp",
        };

    public async Task<string> ImportAsync(
        Guid friendId,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
            throw new FileNotFoundException("The selected avatar image was not found.", fullSourcePath);

        var extension = Path.GetExtension(fullSourcePath);
        if (!SupportedExtensions.Contains(extension))
            throw new ArgumentException("The selected avatar image type is not supported.", nameof(sourcePath));
        if (!HasMatchingImageSignature(fullSourcePath, extension))
            throw new ArgumentException("The selected avatar image content is not valid.", nameof(sourcePath));

        Directory.CreateDirectory(folderPath);
        var destinationPath = Path.Combine(
            folderPath,
            $"{friendId:N}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        var temporaryPath = destinationPath + ".tmp";

        try
        {
            await using var source = File.OpenRead(fullSourcePath);
            await using (var destination = File.Create(temporaryPath))
            {
                await source.CopyToAsync(destination, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, destinationPath);
            return destinationPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public void RemoveIfManaged(string? avatarPath)
    {
        if (string.IsNullOrWhiteSpace(avatarPath))
            return;

        var fullPath = Path.GetFullPath(avatarPath);
        if (File.Exists(fullPath)
            && FriendFolderLayout.IsWithin(fullPath, folderPath))
        {
            File.Delete(fullPath);
        }
    }

    private static bool HasMatchingImageSignature(
        string filePath,
        string extension)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = File.OpenRead(filePath);
        var bytesRead = stream.Read(header);
        return extension.ToLowerInvariant() switch
        {
            ".png" => bytesRead >= 8
                && header[..8].SequenceEqual(
                    new byte[]
                    {
                        0x89,
                        0x50,
                        0x4E,
                        0x47,
                        0x0D,
                        0x0A,
                        0x1A,
                        0x0A,
                    }),
            ".jpg" or ".jpeg" => bytesRead >= 3
                && header[0] == 0xFF
                && header[1] == 0xD8
                && header[2] == 0xFF,
            ".bmp" => bytesRead >= 2
                && header[0] == (byte)'B'
                && header[1] == (byte)'M',
            ".webp" => bytesRead >= 12
                && header[..4].SequenceEqual("RIFF"u8)
                && header[8..12].SequenceEqual("WEBP"u8),
            _ => false,
        };
    }
}

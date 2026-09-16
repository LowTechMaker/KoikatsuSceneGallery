using System.Security.Cryptography;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Hashes the card data appended after the PNG image, which is the card
/// itself — the character, the scene, the outfit.
/// </summary>
/// <remarks>
/// Hashing the whole file answers a different question. A card exported twice,
/// or passed through a tool that re-encodes the preview image, gives two files
/// of different length whose card data is byte for byte the same: same
/// character, same everything the game will load. The preview is a picture of
/// the card, not part of it, so it is left out of the hash.
///
/// The boundary is the same one the parsers use, so a file this cannot find a
/// PNG in is one the rest of the app does not treat as a card either.
/// </remarks>
internal static class CardPayloadHash
{
    /// <summary>
    /// The hash of the appended card data as lower-case hex, or null when
    /// <paramref name="filePath"/> holds no card data or cannot be read.
    /// </summary>
    public static string? TryCompute(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1 << 16,
                FileOptions.SequentialScan);

            var pngSize = PngEmbeddedData.GetPngSize(stream);
            if (pngSize <= 0 || pngSize >= stream.Length)
                return null;

            stream.Seek(pngSize, SeekOrigin.Begin);

            using var sha = SHA256.Create();
            var buffer = new byte[1 << 16];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sha.TransformBlock(buffer, 0, read, null, 0);
            }

            sha.TransformFinalBlock([], 0, 0);
            return Convert.ToHexStringLower(sha.Hash!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // An unreadable file is not a duplicate of anything; the caller
            // falls back to importing it.
            return null;
        }
    }

    /// <summary>
    /// Whether two files hold the same card, ignoring their preview images.
    /// False when either has no card data, so a comparison that could not be
    /// made never reads as a match.
    /// </summary>
    public static bool AreSameCard(
        string leftPath,
        string rightPath,
        CancellationToken cancellationToken = default)
    {
        var left = TryCompute(leftPath, cancellationToken);
        if (left is null)
            return false;

        var right = TryCompute(rightPath, cancellationToken);
        return right is not null && string.Equals(left, right, StringComparison.Ordinal);
    }
}

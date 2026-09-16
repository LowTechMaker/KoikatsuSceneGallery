using System.Security.Cryptography;
using System.Text;

namespace KoikatsuSceneGallery.Helpers;

internal static class FileVersionCacheKey
{
    // Existing on-disk key contract: preserve the supplied path verbatim and use
    // DateTime ticks without converting its kind or normalizing the path.
    public static string Compute(string filePath, DateTime dateModified)
    {
        var input = $"{filePath}|{dateModified.Ticks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16];
    }
}

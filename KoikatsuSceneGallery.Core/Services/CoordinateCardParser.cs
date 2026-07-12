using System.Text;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public static class CoordinateCardParser
{
    private const string KoikatsuMarker = "KoiKatuClothes";

    public static CoordinateMetadata? TryParse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

            long pngSize = PngEmbeddedData.GetPngSize(fs);
            if (pngSize <= 0) return null;
            fs.Seek(pngSize, SeekOrigin.Begin);

            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
            _ = br.ReadInt32();                 // productNo
            string marker = br.ReadString();
            if (!marker.Contains(KoikatsuMarker, StringComparison.Ordinal))
                return null;

            _ = br.ReadString();                // version
            string coordName = br.ReadString(); // coordinate name (may be empty)

            return new CoordinateMetadata(
                string.IsNullOrEmpty(coordName) ? null : coordName);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

namespace KoikatsuSceneGallery.Services;

internal static class ImportDuplicateDetector
{
    public static bool AreFilesIdentical(
        string pathA,
        string pathB,
        CancellationToken cancellationToken = default)
    {
        var infoA = new FileInfo(pathA);
        var infoB = new FileInfo(pathB);
        if (infoA.Length != infoB.Length) return false;

        const int bufferSize = 1 << 16;
        var bufferA = new byte[bufferSize];
        var bufferB = new byte[bufferSize];

        using var streamA = infoA.OpenRead();
        using var streamB = infoB.OpenRead();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readA = streamA.ReadAtLeast(bufferA, bufferSize, throwOnEndOfStream: false);
            var readB = streamB.ReadAtLeast(bufferB, bufferSize, throwOnEndOfStream: false);
            if (readA != readB) return false;
            if (readA == 0) return true;
            if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                return false;
        }
    }
}

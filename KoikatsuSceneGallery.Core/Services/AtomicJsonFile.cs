using System.Text.Json;

namespace KoikatsuSceneGallery.Services;

// Callers own directory creation, write locks, schema/options and logical mutations.
internal static class AtomicJsonFile
{
    public static async Task WriteAsync<T>(
        string path,
        T document,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        try
        {
            temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    options,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
                File.Delete(temporaryPath);
        }
    }
}

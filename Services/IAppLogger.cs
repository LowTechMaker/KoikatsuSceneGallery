namespace KoikatsuSceneGallery.Services;

public interface IAppLogger
{
    void LogError(string operation, Exception exception, string? path = null);
}

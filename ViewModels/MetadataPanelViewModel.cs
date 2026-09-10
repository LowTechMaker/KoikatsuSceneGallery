using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.ViewModels;

public partial class MetadataPanelViewModel : ObservableObject
{
    private string? _path;
    private CancellationTokenSource? _load;
    private long _generation;
    [ObservableProperty] public partial CardMetadataDocument? Document { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "";

    public void SetPath(string? path)
    {
        _generation++;
        _load?.Cancel(); _load?.Dispose(); _load = null;
        _path = path; Document = null; IsLoading = false; StatusText = "";
    }

    public async Task LoadAsync()
    {
        if (string.IsNullOrEmpty(_path) || IsLoading || Document is not null) return;
        long generation = _generation;
        string path = _path;
        _load = new CancellationTokenSource(); var token = _load.Token;
        IsLoading = true; StatusText = UiText.Get("Metadata_Loading");
        try
        {
            var document = await Task.Run(() => CardMetadataReader.TryRead(path, token: token), token);
            token.ThrowIfCancellationRequested();
            if (generation != _generation) return;
            Document = document;
            StatusText = UiText.Get(document is null ? "Metadata_Failed" : document.Summary.IsPartial ? "Metadata_Partial" : "Metadata_Complete");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation == _generation) StatusText = UiText.Get("Metadata_Failed");
            App.Services.GetRequiredService<IAppLogger>().LogError("MetadataPanel.Load", ex, path);
        }
        finally { if (generation == _generation) IsLoading = false; }
    }
}

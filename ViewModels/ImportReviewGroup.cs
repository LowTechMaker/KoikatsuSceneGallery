using System.Collections.ObjectModel;

namespace KoikatsuSceneGallery.ViewModels;

/// <summary>A stable collection for WinUI's virtualized, grouped review list.</summary>
public sealed class ImportReviewGroup(string key) : ObservableCollection<ImportItemReviewState>
{
    public string Key { get; } = key;
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            OnPropertyChanged(new(nameof(Title)));
        }
    }
}

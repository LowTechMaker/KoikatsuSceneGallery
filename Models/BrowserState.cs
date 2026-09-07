using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery.Models;

public sealed record BrowserState(string Query, string? GroupKey, string? GroupTitle, string GroupSearch,
    string? ReturnPath, int ReturnIndex, double ReturnOffset, SortOption Sort, bool Ascending, BrowseContext? Context);

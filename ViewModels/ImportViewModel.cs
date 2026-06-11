using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

public partial class ImportViewModel : ObservableObject
{
    private readonly ImportService _importService;
    private readonly DispatcherQueue _dispatcher;
    private CancellationTokenSource? _cts;

    public ObservableCollection<ImportItem> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsAnalyzing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasItems { get; set; }

    public bool IsEmpty => !HasItems;
    public bool IsIdle => !IsAnalyzing && !IsImporting;

    // Summary counts
    [ObservableProperty]
    public partial int R18GCount { get; set; }

    [ObservableProperty]
    public partial int R18Count { get; set; }

    [ObservableProperty]
    public partial int AllAgesCount { get; set; }

    [ObservableProperty]
    public partial int SceneCount { get; set; }

    [ObservableProperty]
    public partial int CharaCount { get; set; }

    [ObservableProperty]
    public partial int CoordCount { get; set; }

    [ObservableProperty]
    public partial int NotCardCount { get; set; }

    [ObservableProperty]
    public partial int CompletedCount { get; set; }

    [ObservableProperty]
    public partial bool ShowRejectedWarning { get; set; }

    [ObservableProperty]
    public partial int RejectedCount { get; set; }

    private DispatcherTimer? _warningTimer;

    public ImportViewModel(ImportService importService, DispatcherQueue dispatcher)
    {
        _importService = importService;
        _dispatcher = dispatcher;
        Items.CollectionChanged += (_, _) => UpdateCounts();
    }

    [RelayCommand]
    private async Task AddFilesAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0) return;

        var newItems = filePaths
            .Where(p => Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase))
            .Where(p => Items.All(existing => !string.Equals(existing.SourceFilePath, p, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new ImportItem { SourceFilePath = p, Status = ImportItemStatus.Pending })
            .ToList();

        if (newItems.Count == 0) return;

        foreach (var item in newItems)
            Items.Add(item);

        HasItems = Items.Count > 0;
        IsAnalyzing = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            int rejected = await _importService.AnalyzeAsync(Items, _dispatcher, _cts.Token);
            if (rejected > 0)
                ShowRejectedFiles(rejected);
        }
        catch (OperationCanceledException)
        {
            // Expected on re-drop during analysis
        }
        finally
        {
            IsAnalyzing = false;
            UpdateCounts();
        }
    }

    [RelayCommand]
    private async Task ImportAllAsync()
    {
        if (IsImporting) return;
        IsImporting = true;
        _cts = new CancellationTokenSource();

        try
        {
            await _importService.ImportAsync(Items, _dispatcher, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // User cancelled
        }
        finally
        {
            IsImporting = false;
            UpdateCounts();
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _cts?.Cancel();
        Items.Clear();
        HasItems = false;
        UpdateCounts();
    }

    private void ShowRejectedFiles(int count)
    {
        RejectedCount = count;
        ShowRejectedWarning = true;

        _warningTimer?.Stop();
        _warningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _warningTimer.Tick += (_, _) =>
        {
            _warningTimer.Stop();
            ShowRejectedWarning = false;
        };
        _warningTimer.Start();
    }

    private void UpdateCounts()
    {
        var ready = Items.Where(i => i.Status is ImportItemStatus.ReadyToImport or ImportItemStatus.Completed).ToList();
        R18GCount = ready.Count(i => i.Rating == ContentRating.R18G);
        R18Count = ready.Count(i => i.Rating == ContentRating.R18);
        AllAgesCount = ready.Count(i => i.Rating == ContentRating.AllAges);
        SceneCount = Items.Count(i => i.CardType == CardType.Scene);
        CharaCount = Items.Count(i => i.CardType == CardType.Character);
        CoordCount = Items.Count(i => i.CardType == CardType.Coordinate);
        NotCardCount = Items.Count(i => i.CardType == CardType.NotACard);
        CompletedCount = Items.Count(i => i.Status == ImportItemStatus.Completed);
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.ViewModels;

/// <summary>One row of the usage history, resolved against the live library.</summary>
public sealed class UsageHistoryRow(UsageRecord record, CardBase? card)
{
    public UsageRecord Record { get; } = record;

    /// <summary>Null once the file has been moved or deleted since it was recorded.</summary>
    public CardBase? Card { get; } = card;

    public string FileName => Path.GetFileName(Record.FilePath);
    public string WhenText => Record.At.LocalDateTime.ToString("yyyy/MM/dd HH:mm");
    public string OriginText => Record.OriginUrl ?? UiText.Get("Usage_NoOrigin");
    public bool HasOrigin => !string.IsNullOrWhiteSpace(Record.OriginUrl);
    public Uri? ThumbnailUri => Card?.ThumbnailUri;
}

// Internal because it depends on LibraryRegistry, which is app-internal.
internal sealed partial class UsageHistoryViewModel(
    UsageRecordService records,
    LibraryRegistry libraries) : ObservableObject
{
    public ObservableCollection<UsageHistoryRow> Rows { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public bool IsEmpty => !IsLoading && Rows.Count == 0;

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var entries = await records.ReadAsync();
            // Resolved once per load rather than per row: a large library makes
            // the per-row lookup the expensive part otherwise.
            var byPath = libraries.All
                .SelectMany(library => library.Cards)
                .DistinctBy(card => card.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(card => card.FilePath, StringComparer.OrdinalIgnoreCase);

            Rows.Clear();
            foreach (var entry in entries)
            {
                byPath.TryGetValue(entry.FilePath, out var card);
                Rows.Add(new(entry, card));
            }
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public async Task ClearAsync()
    {
        await records.ClearAsync();
        Rows.Clear();
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>The whole log as plain text, one URL per line, for export.</summary>
    public string ToPlainText() => string.Join(Environment.NewLine, Rows.Select(row =>
        $"{row.WhenText}\t{row.FileName}\t{row.Record.OriginUrl ?? ""}"));
}

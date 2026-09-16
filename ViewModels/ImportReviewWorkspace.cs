using System.Collections.ObjectModel;
using System.ComponentModel;
using KoikatsuSceneGallery.Models;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

/// <summary>UI-thread-owned review projection. Does not own or mutate the import item collection.</summary>
internal sealed class ImportReviewWorkspace(Func<string, string> text, Action<Action> dispatch) : IDisposable
{
    private readonly Func<string, string> _text = text;
    private readonly Action<Action> _dispatch = dispatch;
    private bool _updating, _disposed, _queued;
    private long _generation;
    private int _reviewColumns = 4;
    private double _reviewTileWidth = 208;
    public ObservableCollection<ImportItemReviewState> States { get; } = [];
    public ObservableCollection<ImportItemReviewState> VisibleItems { get; } = [];
    public ObservableCollection<ImportReviewRow> Rows { get; } = [];
    public string Filter { get; private set; } = "All";
    public int SelectedCount { get; private set; }
    public bool? AllSelected { get; private set; } = false;
    public event Action? Changed;
    public event Action? RowsChanged;
    public IReadOnlyList<ImportItem> SelectedItems => VisibleItems
        .Where(state => state.CanSelect && state.IsSelected).Select(state => state.Item).ToArray();

    public void Reconcile(IEnumerable<ImportItem> items)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var eligible = items.Distinct(ReferenceEqualityComparer.Instance).Cast<ImportItem>().ToArray();
        var set = eligible.ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var stale in States.Where(state => !set.Contains(state.Item)).ToArray())
        {
            stale.PropertyChanged -= OnStateChanged;
            stale.Dispose();
            States.Remove(stale);
        }
        foreach (var item in eligible)
        {
            if (States.Any(state => ReferenceEquals(state.Item, item))) continue;
            var state = new ImportItemReviewState(item, _text("Import_Review_Unassigned"), _text);
            state.PropertyChanged += OnStateChanged;
            States.Add(state);
        }
        RefreshVisible();
    }

    private bool Matches(ImportItemReviewState state) => Filter switch
    {
        "AllAges" => state.Item.Rating == ContentRating.AllAges,
        "R18" => state.Item.Rating == ContentRating.R18,
        "R18G" => state.Item.Rating == ContentRating.R18G,
        _ => true
    };

    public void SetFilter(string filter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Filter = filter;
        _updating = true;
        try { foreach (var state in States) state.IsSelected = false; }
        finally { _updating = false; }
        RefreshVisible();
    }

    private void RefreshVisible()
    {
        var desired = States.Where(Matches).ToArray();
        var set = desired.ToHashSet();
        foreach (var stale in VisibleItems.Where(state => !set.Contains(state)).ToArray())
            VisibleItems.Remove(stale);
        for (int index = 0; index < desired.Length; index++)
        {
            var current = VisibleItems.IndexOf(desired[index]);
            if (current < 0) VisibleItems.Insert(index, desired[index]);
            else if (current != index) VisibleItems.Move(current, index);
        }
        UpdateSelection();
        QueueRows();
    }

    public void SelectAll(bool? selected)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (selected is null) return;
        Select(VisibleItems, selected.Value);
    }

    public void Toggle(IEnumerable<ImportItemReviewState> items)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var selectable = items.Where(state => VisibleItems.Contains(state) && state.CanSelect).ToArray();
        Select(selectable, !selectable.All(state => state.IsSelected));
    }

    private void Select(IEnumerable<ImportItemReviewState> items, bool value)
    {
        _updating = true;
        try
        {
            foreach (var state in items.Where(state => state.CanSelect)) state.IsSelected = value;
        }
        finally { _updating = false; }
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selectable = VisibleItems.Where(state => state.CanSelect).ToArray();
        SelectedCount = selectable.Count(state => state.IsSelected);
        AllSelected = SelectedCount == 0 ? false : SelectedCount == selectable.Length ? true : null;
        Changed?.Invoke();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || _updating) return;
        if (e.PropertyName == nameof(ImportItemReviewState.Category)) RefreshVisible();
        else if (e.PropertyName == nameof(ImportItemReviewState.IsSelected)) UpdateSelection();
    }

    private void QueueRows()
    {
        if (_disposed || _queued) return;
        _queued = true;
        var generation = _generation;
        _dispatch(() =>
        {
            if (_disposed || generation != _generation) return;
            _queued = false;
            RefreshReviewGroups();
        });
    }

    private void RefreshReviewGroups()
    {
        var visible = VisibleItems.ToArray();
        foreach (var state in visible) state.TileWidth = _reviewTileWidth;
        var desired = Helpers.ImportReviewLayout.Build(visible, s => s.Section, s => s.GroupKey, _reviewColumns);
        var keys = desired.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in Rows.Where(r => !keys.Contains(r.Key)).ToArray()) Rows.Remove(stale);
        var existing = Rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < desired.Count; index++)
        {
            var source = desired[index];
            var found = existing.TryGetValue(source.Key, out var row);
            row ??= new ImportReviewRow(source.Key, source.Kind);
            if (!row.Items.SequenceEqual(source.Items)) row.Items = source.Items;
            if (source.Kind == Helpers.ImportReviewRowKind.Section)
            {
                var resourcePrefix = source.Section switch
                {
                    Helpers.ImportReviewSection.Identified => "Import_Mixed_Identified",
                    Helpers.ImportReviewSection.Unavailable => "Import_Mixed_Unavailable",
                    _ => "Import_Mixed_Attention"
                };
                row.Title = string.Format(_text(resourcePrefix + "Count"), source.Items.Count);
                row.Description = _text(resourcePrefix + (source.Items.Count == 0 ? "Empty" : "Hint"));
            }
            else if (source.Kind == Helpers.ImportReviewRowKind.Group)
                row.Title = string.Format(_text("Import_Review_GroupTitle"), source.Items[0].GroupTitle, source.Items.Count);
            if (!found) Rows.Insert(index, row);
            else if (!ReferenceEquals(Rows[index], row)) Rows.Move(Rows.IndexOf(row), index);
        }
        RowsChanged?.Invoke();
    }

    public void SetReviewViewportWidth(double width)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(width) || width <= 32) return;
        var available = width - 32; // item padding plus the vertical scrollbar
        var columns = Helpers.ImportReviewLayout.ColumnsForWidth(available);
        var tileWidth = Math.Floor((available - (columns - 1) * 12) / columns);
        if (columns == _reviewColumns && Math.Abs(tileWidth - _reviewTileWidth) < 1) return;
        _reviewColumns = columns;
        _reviewTileWidth = tileWidth;
        QueueRows();
    }


    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _generation++;
        _queued = false;
        foreach (var state in States)
        {
            state.PropertyChanged -= OnStateChanged;
            state.Dispose();
        }
        States.Clear();
        VisibleItems.Clear();
        Rows.Clear();
        Filter = "All";
        UpdateSelection();
        RowsChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
        Changed = null;
        RowsChanged = null;
    }
}

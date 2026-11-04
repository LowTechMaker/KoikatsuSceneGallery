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
    // What is left alone is out of the way until asked for.
    private readonly HashSet<Helpers.ImportReviewSection> _collapsed = [Helpers.ImportReviewSection.Skipped];
    private IReadOnlyList<ModReviewItem> _mods = [];
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
        // A set, not a scan per card: with a few thousand cards the scan alone was a few million comparisons.
        var known = States.Select(state => state.Item).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var item in eligible)
        {
            if (known.Contains(item)) continue;
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
        // Usually nothing changed; settling that is one pass, where the reordering below is
        // quadratic in the number of cards.
        if (!VisibleItems.SequenceEqual(desired))
        {
            var set = desired.ToHashSet();
            foreach (var stale in VisibleItems.Where(state => !set.Contains(state)).ToArray())
                VisibleItems.Remove(stale);
            for (int index = 0; index < desired.Length; index++)
            {
                var current = VisibleItems.IndexOf(desired[index]);
                if (current < 0) VisibleItems.Insert(index, desired[index]);
                else if (current != index) VisibleItems.Move(current, index);
            }
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

    /// <summary>Selects one visible review section without carrying a selection from another section.</summary>
    public void ToggleSectionOnly(IEnumerable<ImportItemReviewState> sectionItems)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var section = sectionItems.Where(state => VisibleItems.Contains(state) && state.CanSelect).ToHashSet();
        if (section.Count == 0) return;

        var selected = VisibleItems.Where(state => state.CanSelect && state.IsSelected).ToArray();
        var clear = selected.Length == section.Count && selected.All(section.Contains);
        _updating = true;
        try
        {
            foreach (var state in VisibleItems.Where(state => state.CanSelect))
                state.IsSelected = !clear && section.Contains(state);
        }
        finally { _updating = false; }
        UpdateSelection();
    }

    /// <summary>
    /// Sets the selection of every selectable card at once: those for which
    /// <paramref name="isSelected"/> is true end up selected, the others not. One recount and one
    /// change notification for the whole list, where setting each card's flag on its own would
    /// rescan the list and refresh the page once per card.
    /// </summary>
    public void SetSelection(Func<ImportItemReviewState, bool> isSelected)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _updating = true;
        try
        {
            foreach (var state in States.Where(state => state.CanSelect))
                state.IsSelected = isSelected(state);
        }
        finally { _updating = false; }
        UpdateSelection();
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
        foreach (var row in Rows)
            if (row.Kind is Helpers.ImportReviewRowKind.Section or Helpers.ImportReviewRowKind.Group)
                row.RefreshSelectionAvailability();
        Changed?.Invoke();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || _updating) return;
        if (e.PropertyName == nameof(ImportItemReviewState.Category)) OnCategoryChanged((ImportItemReviewState)sender!);
        else if (e.PropertyName == nameof(ImportItemReviewState.IsSelected)) UpdateSelection();
    }

    // A card's state raises Category for every property of the card that changes, whether or
    // not its category did. Re-sorting the visible list each time made setting one property on
    // every card a quadratic job: seconds on the UI thread for a couple of thousand cards.
    // Only a card that has to appear or disappear needs the list re-sorted; for every other
    // change the selection count and the rows are refreshed once per dispatcher turn.
    private void OnCategoryChanged(ImportItemReviewState state)
    {
        if (Matches(state) != VisibleItems.Contains(state))
        {
            RefreshVisible();
            return;
        }
        // A selected card that can no longer be selected changes the count right now, as the
        // user can see it; everything else about the selection can wait for the turn's refresh.
        if (state.IsSelected && !state.CanSelect) UpdateSelection();
        QueueRefresh();
    }

    private bool _refreshQueued;

    private void QueueRefresh()
    {
        if (_disposed || _refreshQueued) return;
        _refreshQueued = true;
        _dispatch(() =>
        {
            _refreshQueued = false;
            if (_disposed) return;
            UpdateSelection();
            QueueRows();
        });
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

    /// <summary>The mods in the batch, shown as their own section between the identified and the skipped cards.</summary>
    public void SetMods(IReadOnlyList<ModReviewItem> mods)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _mods = mods.ToArray();
        QueueRows();
    }

    /// <summary>Called when a mod's install box changes, so its section header follows.</summary>
    public void RefreshModSelection()
    {
        foreach (var row in Rows.Where(row => row.Section == Helpers.ImportReviewSection.Mods && row.Kind == Helpers.ImportReviewRowKind.Section))
            row.RefreshSelectionAvailability();
    }

    public bool IsCollapsed(Helpers.ImportReviewSection section) => _collapsed.Contains(section);

    public void ToggleCollapsed(Helpers.ImportReviewSection section)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_collapsed.Remove(section)) _collapsed.Add(section);
        QueueRows();
    }

    private void RefreshReviewGroups()
    {
        var visible = VisibleItems.ToArray();
        var cardRows = Helpers.ImportReviewLayout.Build(visible, s => s.Section, s => s.GroupKey, _collapsed);
        var desired = new List<(string Key, Helpers.ImportReviewRowKind Kind, Helpers.ImportReviewSection Section,
            IReadOnlyList<ImportItemReviewState> Items, IReadOnlyList<ModReviewItem> Mods)>();
        foreach (var row in cardRows.Where(r => r.Section < Helpers.ImportReviewSection.Mods))
            desired.Add((row.Key, row.Kind, row.Section, row.Items, []));
        if (_mods.Count > 0)
        {
            desired.Add(("Mods:", Helpers.ImportReviewRowKind.Section, Helpers.ImportReviewSection.Mods, [], _mods));
            if (!_collapsed.Contains(Helpers.ImportReviewSection.Mods))
                foreach (var mod in _mods)
                    desired.Add(("Mods:item:" + mod.SourcePath, Helpers.ImportReviewRowKind.ModItem, Helpers.ImportReviewSection.Mods, [], [mod]));
        }
        foreach (var row in cardRows.Where(r => r.Section > Helpers.ImportReviewSection.Mods))
            desired.Add((row.Key, row.Kind, row.Section, row.Items, []));

        var keys = desired.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in Rows.Where(r => !keys.Contains(r.Key)).ToArray()) Rows.Remove(stale);
        var existing = Rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < desired.Count; index++)
        {
            var source = desired[index];
            var found = existing.TryGetValue(source.Key, out var row);
            row ??= new ImportReviewRow(source.Key, source.Kind) { Section = source.Section };
            if (!row.Items.SequenceEqual(source.Items)) row.Items = source.Items;
            if (!row.Mods.SequenceEqual(source.Mods)) row.Mods = source.Mods;
            row.IsCollapsed = _collapsed.Contains(source.Section);
            row.RefreshSelectionAvailability();
            if (source.Kind == Helpers.ImportReviewRowKind.Section)
            {
                var resourcePrefix = source.Section switch
                {
                    Helpers.ImportReviewSection.Identified => "Import_Mixed_Identified",
                    Helpers.ImportReviewSection.Unavailable => "Import_Mixed_Unavailable",
                    Helpers.ImportReviewSection.Mods => "Import_Mixed_Mods",
                    Helpers.ImportReviewSection.Skipped => "Import_Mixed_Skipped",
                    _ => "Import_Mixed_Attention"
                };
                var count = source.Section == Helpers.ImportReviewSection.Mods ? source.Mods.Count : source.Items.Count;
                row.Title = string.Format(_text(resourcePrefix + "Count"), count);
                row.Description = _text(resourcePrefix + "Hint");
            }
            else if (source.Kind == Helpers.ImportReviewRowKind.Group)
                row.Title = string.Format(_text("Import_Review_GroupTitle"), source.Items[0].GroupTitle, source.Items.Count);
            if (!found) Rows.Insert(index, row);
            else if (!ReferenceEquals(Rows[index], row)) Rows.Move(Rows.IndexOf(row), index);
        }
        RowsChanged?.Invoke();
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
        _mods = [];
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

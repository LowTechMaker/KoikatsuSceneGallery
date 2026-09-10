using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Controls;

public sealed partial class LibraryBrowser
{
    private ComboBox? _metadataSex, _metadataPersonality, _metadataGuid;
    private ComboBox? _sourceFilterCombo;
    private ComboBox? _originFilterCombo;
    private bool _updatingMetadata;
    private bool _scopedMetadataSubscribed;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _metadataOptionsTimer;
    private sealed record FilterOption(object? Value, string Label)
    {
        public override string ToString() => Label;
    }
    private MetadataFilterState? GetMetadataFilters() => VM switch
    {
        CharacterGalleryViewModel c => c.MetadataFilters,
        CoordinateGalleryViewModel c => c.MetadataFilters,
        _ => null
    };
    private void SetScopedMetadataSubscriptions(bool active)
    {
        if (_scope is null || VM is not (CharacterGalleryViewModel or CoordinateGalleryViewModel)
            || active == _scopedMetadataSubscribed) return;
        _scopedMetadataSubscribed = active;
        if (active)
        {
            VM.PropertyChanged += OnScopedMetadataChanged;
            VM.ViewRefreshed += QueueRefresh;
            VM.CardsReloaded += OnScopedMetadataReloaded;
        }
        else
        {
            VM.PropertyChanged -= OnScopedMetadataChanged;
            VM.ViewRefreshed -= QueueRefresh;
            VM.CardsReloaded -= OnScopedMetadataReloaded;
        }
    }
    private void OnScopedMetadataChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => QueueRefresh();
    private void OnScopedMetadataReloaded() { _keys.Clear(); QueueRefresh(); }
    private void BuildMetadataFilters()
    {
        var filters = GetMetadataFilters();
        if (filters is null) return;
        ComboBox Create(string key)
        {
            var box = new ComboBox { Header = UiText.Get(key), HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 180 };
            SpecificFilters.Children.Add(box); return box;
        }
        if (VM is CharacterGalleryViewModel)
        {
            _metadataSex = Create("Metadata_Sex");
            _metadataSex.SelectionChanged += (_, _) =>
            {
                if (!_updatingMetadata && _metadataSex.SelectedItem is FilterOption option) filters.Sex = option.Value as int?;
            };
            _metadataPersonality = Create("Metadata_Personality");
            _metadataPersonality.SelectionChanged += (_, _) =>
            {
                if (!_updatingMetadata && _metadataPersonality.SelectedItem is FilterOption option) filters.Personality = option.Value as int?;
            };
        }
        _metadataGuid = Create("Metadata_PluginGuid");
        _metadataGuid.SelectionChanged += (_, _) =>
        {
            if (!_updatingMetadata && _metadataGuid.SelectedItem is FilterOption option) filters.PluginGuid = option.Value as string;
        };
        _metadataOptionsTimer = DispatcherQueue.CreateTimer();
        _metadataOptionsTimer.Interval = TimeSpan.FromMilliseconds(750);
        _metadataOptionsTimer.IsRepeating = true;
        _metadataOptionsTimer.Tick += (_, _) => UpdateMetadataOptions();
        FilterButton.Flyout.Opened += (_, _) => { UpdateMetadataOptions(); _metadataOptionsTimer.Start(); };
        FilterButton.Flyout.Closed += (_, _) => _metadataOptionsTimer.Stop();
        UpdateMetadataOptions();
    }

    private void UpdateMetadataOptions()
    {
        var filters = GetMetadataFilters();
        if (filters is null || _metadataGuid is null) return;
        var summaries = _adapter!.Cards.Select(c => c switch
        {
            CharacterCard cc when cc.MetadataLoaded => cc.MetadataSummary,
            CoordinateCard cc when cc.MetadataLoaded => cc.MetadataSummary,
            _ => null
        }).OfType<CardMetadataSummary>().ToArray();
        var all = new FilterOption(null, UiText.Get("Gallery_FilterAll.Content"));
        void Set(ComboBox? box, IEnumerable<FilterOption> options, object? selected)
        {
            if (box is null) return;
            var list = new[] { all }.Concat(options).ToList();
            if (selected is not null && !list.Any(o => Equals(o.Value, selected))) list.Add(new(selected, selected.ToString()!));
            if (!box.Items.OfType<FilterOption>().SequenceEqual(list))
            {
                box.Items.Clear(); foreach (var item in list) box.Items.Add(item);
            }
            box.SelectedIndex = list.FindIndex(o => Equals(o.Value, selected));
        }
        _updatingMetadata = true;
        try
        {
            Set(_metadataSex, [new(0, UiText.Get("Common_Male")), new(1, UiText.Get("Common_Female")), new(-1, UiText.Get("Common_Unknown"))], filters.Sex);
            var personalities = summaries.Select(s => s.PersonalityId ?? -1).Append(-1).Distinct().Order()
                .Select(id => new FilterOption(id, id == -1 ? UiText.Get("Common_Unknown") : $"{id} · {CardMetadataExport.PersonalityName(id, GameVersion.KoikatsuSunshine)}"));
            Set(_metadataPersonality, personalities, filters.Personality);
            Set(_metadataGuid, summaries.SelectMany(s => s.PluginGuids).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
                .Select(guid => new FilterOption(guid, guid)), filters.PluginGuid);
        }
        finally { _updatingMetadata = false; }
    }
}

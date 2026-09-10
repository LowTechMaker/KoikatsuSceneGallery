using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Controls;

public sealed class MetadataPanel : UserControl
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(MetadataPanel), new PropertyMetadata(null, OnPathChanged));
    public string? FilePath { get => (string?)GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }
    private readonly MetadataPanelViewModel _vm = new();
    private readonly Expander _expander = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock _basic = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly ProgressRing _progress = new() { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TreeView _tree = new() { MaxHeight = 400, SelectionMode = TreeViewSelectionMode.None };
    private readonly Button _json = new();
    private readonly Button _csv = new();
    private readonly Button _retry = new();
    private CardMetadataDocument? _rendered;
    private CancellationTokenSource? _export;
    private bool _exporting;

    public static bool ContainsFocus(XamlRoot root)
    {
        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) as DependencyObject;
        while (current is not null)
        {
            if (current is MetadataPanel) return true;
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    public MetadataPanel()
    {
        _expander.Header = UiText.Get("Metadata_Title");
        _json.Content = UiText.Get("Metadata_ExportJson");
        _csv.Content = UiText.Get("Metadata_ExportCsv");
        _retry.Content = UiText.Get("Metadata_Retry");
        var body = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(_progress); body.Children.Add(_status); body.Children.Add(_retry);
        body.Children.Add(_basic); body.Children.Add(_tree);
        body.Children.Add(_json); body.Children.Add(_csv);
        body.Children.Add(new TextBlock { Text = UiText.Get("Metadata_CsvNote"), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        _expander.Content = body; Content = _expander;
        _vm.PropertyChanged += (_, _) => Render();
        _expander.Expanding += (_, _) => Load();
        _retry.Click += (_, _) => { _vm.SetPath(FilePath); Load(); };
        _json.Click += (_, _) => Export(false);
        _csv.Click += (_, _) => Export(true);
        _tree.Expanding += (_, e) => Populate(e.Node);
        Loaded += (_, _) => { _vm.SetPath(FilePath); if (_expander.IsExpanded) Load(); };
        Unloaded += (_, _) => { _export?.Cancel(); _vm.SetPath(null); };
        Render();
    }

    private static void OnPathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (MetadataPanel)sender;
        panel._export?.Cancel(); panel._vm.SetPath((string?)args.NewValue);
        if (panel.IsLoaded && panel._expander.IsExpanded) panel.Load();
    }
    private void Load() => _vm.LoadAsync().Observe(App.Services.GetRequiredService<IAppLogger>(), "MetadataPanel.Load");

    private void Render()
    {
        _status.Text = _vm.StatusText;
        _progress.IsActive = _vm.IsLoading; _progress.Visibility = _vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        _retry.Visibility = !_vm.IsLoading && _vm.Document is null ? Visibility.Visible : Visibility.Collapsed;
        _json.IsEnabled = _csv.IsEnabled = !_vm.IsLoading && _vm.Document is not null && !_exporting;
        _json.Content = UiText.Get(_vm.Document?.Summary.IsPartial == true ? "Metadata_ExportPartialJson" : "Metadata_ExportJson");
        if (ReferenceEquals(_rendered, _vm.Document)) return;
        _rendered = _vm.Document; _tree.RootNodes.Clear(); _basic.Text = "";
        if (_rendered is not { } doc) return;
        var s = doc.Summary;
        string Unknown(string? value) => string.IsNullOrEmpty(value) ? UiText.Get("Common_Unknown") : value;
        var lines = new List<string> { s.Name ?? "", s.CardType, UiText.Get("Metadata_Version") + ": " + Unknown(s.Version) };
        if (s.CardType != "KoikatuClothes")
        {
            lines.Add(UiText.Get("Metadata_Sex") + ": " + UiText.Get(s.Sex switch { 0 => "Common_Male", 1 => "Common_Female", _ => "Common_Unknown" }));
            lines.Add(UiText.Get("Metadata_Personality") + ": " + (s.PersonalityId is int id ? $"{id} · {CardMetadataExport.PersonalityName(id, s.Game)}" : UiText.Get("Common_Unknown")));
        }
        if (s.Game == GameVersion.KoikatsuSunshine)
        {
            lines.Add(UiText.Get("Metadata_Language") + ": " + (s.Language?.ToString() ?? UiText.Get("Common_Unknown")));
            lines.Add("UserID: " + Unknown(s.UserId)); lines.Add("DataID: " + Unknown(s.DataId));
        }
        lines.Add(UiText.Get("Metadata_Extended") + $": {s.PluginGuids.Length} · {CardMetadataExport.FormatSize(s.ExtendedSize)}");
        _basic.Text = string.Join("\n", lines);
        foreach (var block in doc.Blocks)
        {
            var node = new TreeViewNode();
            node.Content = block.Data is not null ? new TreeItem($"{block.Name} ({CardMetadataExport.FormatSize(block.Size)})", block.Data, IsPluginMap: block.Name == "KKEx")
                : new TreeItem($"{block.Name}: {UiText.Get("Metadata_Unavailable")} ({block.Diagnostic})", null);
            node.HasUnrealizedChildren = block.Data is not null; _tree.RootNodes.Add(node);
        }
    }

    private sealed record TreeItem(string Label, MetadataValue? Value, int Offset = 0, bool IsPluginMap = false)
    {
        public override string ToString() => Label;
    }
    private static string Preview(MetadataValue value)
    {
        if (value.Kind is "binary" or "extension")
        {
            var text = value.Value ?? "";
            int size = text.Length / 4 * 3 - (text.EndsWith("==") ? 2 : text.EndsWith('=') ? 1 : 0);
            return $"{value.Kind} ({size} B): {text[..Math.Min(32, text.Length)]}…";
        }
        if (value.Entries is not null) return $"map ({value.Entries.Count})";
        if (value.Items is not null) return $"array ({value.Items.Count})";
        string raw = value.Value ?? value.Kind;
        return raw.Length > 256 ? raw[..256] + "…" : raw;
    }
    private void Populate(TreeViewNode parent)
    {
        if (!parent.HasUnrealizedChildren || parent.Content is not TreeItem { Value: { } value } item) return;
        parent.HasUnrealizedChildren = false;
        var children = value.Entries is { } entries
            ? entries.Select(e => (Preview(e.Key), e.Value))
            : value.Items is { } items ? items.Select((v, i) => (i.ToString(), v)) : [("value", value)];
        var page = children.Skip(item.Offset).Take(101).ToArray();
        foreach (var (key, child) in page.Take(100))
        {
            var plugin = item.IsPluginMap ? _rendered?.Summary.Plugins.FirstOrDefault(p => p.Guid == key) : null;
            string label = plugin is null ? key + ": " + Preview(child)
                : $"{key} · v{plugin.Version?.ToString() ?? "?"} · {plugin.Size} B";
            parent.Children.Add(new TreeViewNode
            {
                Content = new TreeItem(label, child),
                HasUnrealizedChildren = child.Items is not null || child.Entries is not null
            });
        }
        if (page.Length > 100) parent.Children.Add(new TreeViewNode
        {
            Content = new TreeItem(UiText.Get("Metadata_More"), value, item.Offset + 100, item.IsPluginMap), HasUnrealizedChildren = true
        });
    }

    private void Export(bool csv) => ExportAsync(csv).Observe(App.Services.GetRequiredService<IAppLogger>(), "MetadataPanel.Export");
    private async Task ExportAsync(bool csv)
    {
        if (_exporting || _vm.Document is not { } document) return;
        _exporting = true; _export = new CancellationTokenSource(); var token = _export.Token; Render();
        try
        {
            string extension = csv ? ".csv" : ".json";
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = Path.GetFileNameWithoutExtension(document.FileName) + (document.Summary.IsPartial ? ".partial.metadata" : ".metadata")
            };
            picker.FileTypeChoices.Add(csv ? "KKManager CSV" : "Metadata JSON", new List<string> { extension });
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                Microsoft.UI.Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId));
            var file = await picker.PickSaveFileAsync();
            if (file is null || token.IsCancellationRequested || !ReferenceEquals(_vm.Document, document)) return;
            if (string.Equals(Path.GetFullPath(file.Path), document.FileName, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Cannot overwrite the source card.");
            await CardMetadataExport.WriteAsync(document, file.Path, csv, token);
            if (ReferenceEquals(_vm.Document, document)) _vm.StatusText = UiText.Get("Metadata_Exported");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_vm.Document, document)) _vm.StatusText = UiText.Get("Metadata_ExportFailed");
            App.Services.GetRequiredService<IAppLogger>().LogError("MetadataPanel.Export", ex);
        }
        finally { _export.Dispose(); _export = null; _exporting = false; Render(); }
    }
}

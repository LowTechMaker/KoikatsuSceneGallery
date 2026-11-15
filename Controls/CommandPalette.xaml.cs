using KoikatsuSceneGallery.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;

namespace KoikatsuSceneGallery.Controls;

/// <summary>One row in the palette.</summary>
public sealed class PaletteEntry(string glyph, string title, string detail, string categoryLabel, Action invoke)
{
    public string Glyph { get; } = glyph;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public string CategoryLabel { get; } = categoryLabel;
    public Action Invoke { get; } = invoke;
}

/// <summary>
/// The Ctrl+K overlay. The window supplies what can be found
/// (<see cref="Source"/>); this control only asks, ranks and runs.
/// </summary>
public sealed partial class CommandPalette : UserControl
{
    private const int PerCategory = 6;
    private const int MaxResults = 24;

    private IReadOnlyList<PaletteCandidate<PaletteEntry>> _candidates = [];
    private DispatcherQueueTimer? _debounce;
    private FrameworkElement? _restoreFocus;

    public CommandPalette()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Everything that can be found, asked for each time the palette opens:
    /// what is on offer changes with the platform, the plugins and the library.
    /// </summary>
    public Func<IReadOnlyList<PaletteCandidate<PaletteEntry>>>? Source { get; set; }

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Open()
    {
        if (IsOpen)
        {
            QueryBox.SelectAll();
            QueryBox.Focus(FocusState.Programmatic);
            return;
        }

        _restoreFocus = XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as FrameworkElement : null;
        _candidates = Source?.Invoke() ?? [];
        Visibility = Visibility.Visible;
        QueryBox.Text = "";
        Refresh();
        QueryBox.Focus(FocusState.Programmatic);
    }

    public void Close()
    {
        if (!IsOpen) return;
        _debounce?.Stop();
        Visibility = Visibility.Collapsed;
        // Let go of the library: the candidates hold every card.
        _candidates = [];
        ResultsList.ItemsSource = null;
        _restoreFocus?.Focus(FocusState.Programmatic);
        _restoreFocus = null;
    }

    private void Refresh()
    {
        var ranked = CommandPaletteRanker.Rank(QueryBox.Text, _candidates, PerCategory, MaxResults);
        var entries = ranked.Select(r => r.Payload).ToList();
        ResultsList.ItemsSource = entries;
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (entries.Count > 0) ResultsList.SelectedIndex = 0;
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Ranking every card in a large library on each keystroke is cheap but
        // not free; typing a word should rank once, not once per letter.
        if (_debounce is null)
        {
            _debounce = DispatcherQueue.CreateTimer();
            _debounce.Interval = TimeSpan.FromMilliseconds(120);
            _debounce.IsRepeating = false;
            _debounce.Tick += (_, _) => { if (IsOpen) Refresh(); };
        }

        _debounce.Stop();
        _debounce.Start();
    }

    private void QueryBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                Move(1);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                Move(-1);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                // A query typed faster than the debounce still runs against
                // what is on screen; rank now so Enter acts on what was typed.
                if (_debounce?.IsRunning == true)
                {
                    _debounce.Stop();
                    Refresh();
                }

                Run(ResultsList.SelectedItem as PaletteEntry);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void Move(int step)
    {
        var count = ResultsList.Items.Count;
        if (count == 0) return;
        var next = (ResultsList.SelectedIndex + step + count) % count;
        ResultsList.SelectedIndex = next;
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void ResultsList_ItemClick(object sender, ItemClickEventArgs e) => Run(e.ClickedItem as PaletteEntry);

    private void Run(PaletteEntry? entry)
    {
        if (entry is null) return;
        // Close first: the action usually navigates, and focus should go to
        // the new page rather than back to whatever had it before.
        _restoreFocus = null;
        Close();
        entry.Invoke();
    }

    private void Scrim_Tapped(object sender, TappedRoutedEventArgs e) => Close();
}

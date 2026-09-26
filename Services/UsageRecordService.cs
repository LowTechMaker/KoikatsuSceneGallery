using System.Collections.Concurrent;
using System.Text;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Records which cards left the app — dragged out, or copied — together with
/// the page they came from, while record mode is on.
/// </summary>
/// <remarks>
/// Locally collected cards are skipped outright. They have no origin page by
/// design (see <see cref="CardOrigin.LinksFor"/>), and the user asked for them
/// to stay out of the log entirely rather than appear with an empty URL.
///
/// Recording happens on the UI thread inside a drag, so nothing here touches
/// the disk synchronously: entries queue in memory and a single background
/// writer appends them.
/// </remarks>
public sealed class UsageRecordService
{
    private const string LogFileName = "usage_log.jsonl";

    private readonly string _logPath;
    private readonly Func<bool> _isEnabled;
    private readonly Func<IReadOnlyDictionary<string, CardBase>> _cardsByPath;
    private readonly IAppLogger _logger;
    private readonly ConcurrentQueue<UsageRecord> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public UsageRecordService(
        SettingsViewModel settings,
        Func<IReadOnlyDictionary<string, CardBase>> cardsByPath,
        IAppLogger logger)
        : this(() => settings.RecordModeEnabled, cardsByPath, logger,
               Path.Combine(AppPaths.LocalFolder, LogFileName))
    {
    }

    /// <summary>
    /// Takes the switch as a delegate rather than the whole settings view
    /// model, which is what lets the tests drive it without a UI thread.
    /// </summary>
    internal UsageRecordService(
        Func<bool> isEnabled,
        Func<IReadOnlyDictionary<string, CardBase>> cardsByPath,
        IAppLogger logger,
        string logPath)
    {
        _isEnabled = isEnabled;
        _cardsByPath = cardsByPath;
        _logger = logger;
        _logPath = logPath;
    }

    /// <summary>Raised after entries reach the log, so an open history page can refresh.</summary>
    public event Action? Recorded;

    /// <summary>
    /// Records the given files, if record mode is on. Returns immediately; the
    /// append runs in the background.
    /// </summary>
    public void Record(IEnumerable<string> filePaths, string operation)
    {
        if (!_isEnabled()) return;

        var cards = _cardsByPath();
        var at = DateTimeOffset.Now;
        var queued = 0;
        foreach (var path in filePaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            cards.TryGetValue(path, out var card);
            if (CardOrigin.IsLocal(card)) continue;

            var links = FilenameLinkParser.Parse(path);
            _pending.Enqueue(new(at, operation, path, links.PixivUrl ?? links.BepisDbUrl));
            queued++;
        }

        if (queued > 0) FlushAsync().Observe(_logger, "UsageRecord.Flush");
    }

    /// <summary>Waits for queued entries to reach disk. For tests.</summary>
    internal Task DrainAsync() => FlushAsync();

    /// <summary>Everything recorded so far, newest first.</summary>
    public async Task<IReadOnlyList<UsageRecord>> ReadAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            if (!File.Exists(_logPath)) return [];
            var lines = await File.ReadAllLinesAsync(_logPath, Encoding.UTF8);
            var records = UsageLogFormat.Parse(lines);
            records.Reverse();
            return records;
        }
        catch (Exception ex)
        {
            _logger.LogError("UsageRecord.Read", ex, _logPath);
            return [];
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task ClearAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            while (_pending.TryDequeue(out _)) { }
            if (File.Exists(_logPath)) File.Delete(_logPath);
        }
        catch (Exception ex)
        {
            _logger.LogError("UsageRecord.Clear", ex, _logPath);
        }
        finally
        {
            _writeLock.Release();
        }
        Recorded?.Invoke();
    }

    private async Task FlushAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            var batch = new List<string>();
            while (_pending.TryDequeue(out var record)) batch.Add(UsageLogFormat.Serialize(record));
            if (batch.Count == 0) return;

            await File.AppendAllLinesAsync(_logPath, batch, Encoding.UTF8);
            await TrimIfOversizedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("UsageRecord.Append", ex, _logPath);
        }
        finally
        {
            _writeLock.Release();
        }
        Recorded?.Invoke();
    }

    /// <summary>
    /// Rewrites the log once it outgrows the cap. Checked by line count rather
    /// than on every append so the common path stays a pure append.
    /// </summary>
    private async Task TrimIfOversizedAsync()
    {
        var lines = await File.ReadAllLinesAsync(_logPath, Encoding.UTF8);
        if (lines.Length <= UsageLogFormat.MaxEntries) return;

        var kept = UsageLogFormat.Trim(UsageLogFormat.Parse(lines));
        var temp = _logPath + ".tmp";
        await File.WriteAllLinesAsync(temp, kept.Select(UsageLogFormat.Serialize), Encoding.UTF8);
        File.Move(temp, _logPath, overwrite: true);
    }
}

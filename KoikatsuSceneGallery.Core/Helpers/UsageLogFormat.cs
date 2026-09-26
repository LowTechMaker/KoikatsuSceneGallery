using System.Text.Json;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// The on-disk shape of the usage log: one JSON object per line.
/// </summary>
/// <remarks>
/// Append-only by line rather than a rewritten document, because the log only
/// ever grows and a torn write then costs the last entry instead of the file.
/// Parsing skips damaged lines for the same reason.
/// </remarks>
public static class UsageLogFormat
{
    /// <summary>Entries kept on disk; older ones are dropped when the log is rewritten.</summary>
    public const int MaxEntries = 20_000;

    private static readonly JsonSerializerOptions Options = new();

    public static string Serialize(UsageRecord record) => JsonSerializer.Serialize(record, Options);

    /// <summary>Reads a log, newest last, skipping any line that no longer parses.</summary>
    public static List<UsageRecord> Parse(IEnumerable<string> lines)
    {
        var records = new List<UsageRecord>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                if (JsonSerializer.Deserialize<UsageRecord>(line, Options) is { } record)
                    records.Add(record);
            }
            catch (JsonException)
            {
                // A half-written final line is expected after a crash.
            }
        }
        return records;
    }

    /// <summary>Keeps only the newest <see cref="MaxEntries"/>, so the log cannot grow without bound.</summary>
    public static IReadOnlyList<UsageRecord> Trim(IReadOnlyList<UsageRecord> records, int max = MaxEntries)
        => records.Count <= max ? records : records.Skip(records.Count - max).ToArray();
}

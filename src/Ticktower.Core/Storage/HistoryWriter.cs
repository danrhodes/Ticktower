using System.Text.Json;
using Ticktower.Core.Models;

namespace Ticktower.Core.Storage;

/// <summary>One line of <c>history/YYYY-MM.jsonl</c>: a closed timer with its full event log.</summary>
public sealed class HistoryRecord
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DateTime ClosedUtc { get; set; }
    public TimerItem Timer { get; set; } = new();
}

/// <summary>
/// Appends closed timers to monthly JSONL files under <c>history/</c>. Append-only, one JSON object per line,
/// filed by the month (UTC) the timer closed in. Existing lines are never rewritten.
/// </summary>
public sealed class HistoryWriter
{
    private static readonly JsonSerializerOptions LineOptions = new(JsonFileStore.Options) { WriteIndented = false };

    private readonly string _historyDir;

    /// <param name="dataDir">The Ticktower data folder (<c>%AppData%\Ticktower</c>).</param>
    public HistoryWriter(string dataDir)
    {
        _historyDir = Path.Combine(dataDir, "history");
    }

    public void Append(TimerItem timer, DateTime closedUtc)
    {
        if (closedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Close time must be UTC.", nameof(closedUtc));

        Directory.CreateDirectory(_historyDir);
        var line = JsonSerializer.Serialize(new HistoryRecord { ClosedUtc = closedUtc, Timer = timer }, LineOptions);
        using var fs = new FileStream(PathFor(closedUtc.Year, closedUtc.Month), FileMode.Append, FileAccess.Write, FileShare.Read);
        using var w = new StreamWriter(fs);
        w.Write(line);
        w.Write('\n');
        w.Flush();
        fs.Flush(flushToDisk: true);
    }

    /// <summary>Reads a month in append order. A line that fails to parse is skipped so one bad write cannot hide the rest.</summary>
    public IReadOnlyList<HistoryRecord> ReadMonth(int year, int month)
    {
        var path = PathFor(year, month);
        var records = new List<HistoryRecord>();
        if (!File.Exists(path)) return records;

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var r = JsonSerializer.Deserialize<HistoryRecord>(line, JsonFileStore.Options);
                if (r is not null) records.Add(r);
            }
            catch (JsonException) { }
        }
        return records;
    }

    private string PathFor(int year, int month) => Path.Combine(_historyDir, $"{year:D4}-{month:D2}.jsonl");
}

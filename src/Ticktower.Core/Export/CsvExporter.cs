using System.Globalization;
using System.Text;
using Ticktower.Core.Engine;
using Ticktower.Core.Models;
using Ticktower.Core.Storage;

namespace Ticktower.Core.Export;

/// <param name="IsUserText">Free text typed by a person (name, notes, ...), as opposed to a number or date we generated.</param>
public readonly record struct ExportCell(string Text, bool IsUserText = false);

public sealed record ExportTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<ExportCell>> Rows);

/// <summary>Turns closed-timer history into a table by applying an <see cref="ExportProfile"/>, then writes it as CSV.</summary>
public static class CsvExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Problems that would make the profile unusable, in plain language. Empty means valid.</summary>
    public static List<string> Validate(ExportProfile p)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(p.Delimiter) || p.Delimiter.Any(c => c is '"' or '\r' or '\n'))
            errors.Add("The delimiter must be one or more characters, not a quote or a line break.");
        if (!ExportFormats.IsValidDatePattern(p.DateFormat))
            errors.Add($"Date format \"{p.DateFormat}\" is not valid. Use \"iso\" or a pattern such as yyyy-MM-dd HH:mm.");
        if (!p.Columns.Any(c => c.Enabled))
            errors.Add("Enable at least one column.");

        foreach (var c in p.Columns.Where(c => !string.IsNullOrWhiteSpace(c.Format)))
        {
            if (c.Source == ExportSource.Duration)
            {
                if (!ExportFormats.TryParseDuration(c.Format, out _))
                    errors.Add($"Column \"{c.Header}\": duration format must be hh:mm:ss, decimal-hours or minutes.");
            }
            else if (ExportFormats.IsDateSource(c.Source) && !ExportFormats.IsValidDatePattern(c.Format))
            {
                errors.Add($"Column \"{c.Header}\": date format \"{c.Format}\" is not valid.");
            }
        }
        return errors;
    }

    /// <summary>Applies the profile's columns, row mode and formats to the records.</summary>
    /// <param name="typeNames">Type id to display name; unknown ids fall back to the id.</param>
    public static ExportTable Build(ExportProfile profile, IEnumerable<HistoryRecord> records, IReadOnlyDictionary<string, string> typeNames)
    {
        var errors = Validate(profile);
        if (errors.Count > 0) throw new InvalidOperationException(errors[0]);

        var columns = profile.Columns.Where(c => c.Enabled).ToList();
        var rows = new List<IReadOnlyList<ExportCell>>();

        foreach (var record in records)
        {
            var timer = record.Timer;
            if (profile.RowMode == ExportRowMode.PerTimer || timer.Events.Count == 0)
            {
                rows.Add(columns.Select(c => Cell(c, profile, record, typeNames, null)).ToList());
            }
            else
            {
                foreach (var e in timer.Events)
                    rows.Add(columns.Select(c => Cell(c, profile, record, typeNames, e)).ToList());
            }
        }
        return new ExportTable(columns.Select(c => c.Header).ToList(), rows);
    }

    /// <summary>The end of a timer's work: the stop (stopwatch) or last acknowledge (countdown, interval); otherwise when it was closed.</summary>
    public static DateTime EndOf(HistoryRecord r)
    {
        var t = r.Timer;
        var wanted = t.Kind == TimerKind.Stopwatch ? TimerEventType.Stop : TimerEventType.Ack;
        var last = t.Events.LastOrDefault(e => e.Type == wanted);
        return last?.Utc ?? r.ClosedUtc;
    }

    /// <summary>Stopwatch: running time excluding pauses. Countdown and interval: start to end.</summary>
    public static TimeSpan? DurationOf(HistoryRecord r)
    {
        var t = r.Timer;
        if (StartOf(r) is not { } start) return null;
        var end = EndOf(r);
        if (t.Kind == TimerKind.Stopwatch)
            return TimerStateMachine.Elapsed(t, UtcOf(r.ClosedUtc));
        return end > start ? end - start : TimeSpan.Zero;
    }

    public static DateTime? StartOf(HistoryRecord r) =>
        r.Timer.StartedUtc ?? r.Timer.Events.FirstOrDefault(e => e.Type == TimerEventType.Start)?.Utc;

    public static string FormatDate(DateTime utc, string pattern) =>
        UtcOf(utc).ToString(pattern.Equals(ExportFormats.Iso, StringComparison.OrdinalIgnoreCase) ? ExportFormats.IsoPattern : pattern, Inv);

    public static string FormatDuration(TimeSpan span, DurationFormat format)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return format switch
        {
            DurationFormat.DecimalHours => span.TotalHours.ToString("F2", Inv),
            DurationFormat.Minutes => span.TotalMinutes.ToString("F2", Inv),
            _ => $"{(long)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}",
        };
    }

    private static ExportCell Cell(ExportColumn c, ExportProfile p, HistoryRecord r, IReadOnlyDictionary<string, string> typeNames, TimerEvent? ev)
    {
        var t = r.Timer;
        var datePattern = string.IsNullOrWhiteSpace(c.Format) ? p.DateFormat : c.Format;

        return c.Source switch
        {
            ExportSource.Name => User(t.Name),
            ExportSource.Ticket => User(t.Ticket),
            ExportSource.Client => User(t.Client),
            ExportSource.Notes => User(t.Notes),
            ExportSource.Priority => new(t.Priority.ToString()),
            ExportSource.Type => User(t.TypeId is null ? "" : typeNames.GetValueOrDefault(t.TypeId, t.TypeId)),
            ExportSource.StartUtc => new(StartOf(r) is { } s ? FormatDate(s, datePattern) : ""),
            ExportSource.EndUtc => new(StartOf(r) is null ? "" : FormatDate(EndOf(r), datePattern)),
            ExportSource.Duration => new(DurationOf(r) is { } d
                ? FormatDuration(d, ExportFormats.TryParseDuration(c.Format, out var f) ? f : p.DurationFormat)
                : ""),
            ExportSource.Laps => new(TimerStateMachine.LapCount(t).ToString(Inv)),
            ExportSource.Bookmarks => new(TimerStateMachine.BookmarkCount(t).ToString(Inv)),
            ExportSource.Snoozes => new(t.Events.Count(e => e.Type == TimerEventType.Snooze).ToString(Inv)),
            ExportSource.Acknowledgements => new(t.Events.Count(e => e.Type == TimerEventType.Ack).ToString(Inv)),
            ExportSource.LapTimes => new(string.Join("; ", t.Events.Where(e => e.Type == TimerEventType.Lap).Select(e => FormatDate(e.Utc, datePattern)))),
            ExportSource.BookmarkDetails => User(string.Join("; ", t.Events.Where(e => e.Type == TimerEventType.Bookmark)
                .Select(e => string.IsNullOrWhiteSpace(e.Note) ? FormatDate(e.Utc, datePattern) : $"{FormatDate(e.Utc, datePattern)} {e.Note}"))),
            ExportSource.EventType => new(ev?.Type.ToString() ?? ""),
            ExportSource.EventUtc => new(ev is null ? "" : FormatDate(ev.Utc, datePattern)),
            ExportSource.EventNote => User(ev?.Note ?? ""),
            _ => new(""),
        };
    }

    private static ExportCell User(string text) => new(text, IsUserText: true);

    private static DateTime UtcOf(DateTime d) => d.Kind == DateTimeKind.Utc ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc);

    // ---- CSV text ----

    public static void Write(TextWriter writer, ExportProfile profile, ExportTable table)
    {
        if (profile.IncludeHeader)
            WriteLine(writer, profile, table.Headers.Select(h => new ExportCell(h)), isHeader: true);
        foreach (var row in table.Rows)
            WriteLine(writer, profile, row, isHeader: false);
    }

    public static string ToCsv(ExportProfile profile, ExportTable table)
    {
        var sw = new StringWriter { NewLine = "\r\n" };
        Write(sw, profile, table);
        return sw.ToString();
    }

    public static Encoding EncodingFor(CsvEncodingKind kind) => kind switch
    {
        CsvEncodingKind.Utf8 => new UTF8Encoding(false),
        CsvEncodingKind.Latin1 => Encoding.Latin1,
        CsvEncodingKind.Utf16 => new UnicodeEncoding(false, true),
        _ => new UTF8Encoding(true),
    };

    /// <summary>Writes the CSV via a temp file and replace, so a failed export never leaves a half-written file.</summary>
    public static void WriteFile(string path, ExportProfile profile, ExportTable table)
    {
        var encoding = EncodingFor(profile.Encoding);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new StreamWriter(fs, encoding) { NewLine = "\r\n" })
        {
            Write(w, profile, table);
        }
        File.Move(tmp, path, overwrite: true);
    }

    private static void WriteLine(TextWriter w, ExportProfile p, IEnumerable<ExportCell> cells, bool isHeader)
    {
        w.Write(string.Join(p.Delimiter, cells.Select(c => Field(c, p, isHeader))));
        w.Write("\r\n");
    }

    private static string Field(ExportCell cell, ExportProfile p, bool isHeader)
    {
        var text = cell.Text;
        if (p.ProtectFormulas && cell.IsUserText && text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            text = "'" + text;

        var quote = p.QuoteStyle switch
        {
            CsvQuoteStyle.All => true,
            CsvQuoteStyle.NonNumeric => isHeader || cell.IsUserText || !double.TryParse(text, NumberStyles.Float, Inv, out _),
            _ => false,
        };
        quote |= text.Contains(p.Delimiter, StringComparison.Ordinal) || text.AsSpan().IndexOfAny('"', '\r', '\n') >= 0
            || (text.Length > 0 && (text[0] == ' ' || text[^1] == ' '));

        return quote ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }
}

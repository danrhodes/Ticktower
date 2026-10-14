namespace Ticktower.Core.Models;

public enum CsvQuoteStyle { Minimal, All, NonNumeric }

public enum CsvEncodingKind { Utf8Bom, Utf8, Latin1, Utf16 }

public enum ExportRowMode { PerTimer, PerEvent }

public enum DurationFormat { HhMmSs, DecimalHours, Minutes }

public enum ExportSource
{
    Name,
    Ticket,
    Client,
    Priority,
    Type,
    StartUtc,
    EndUtc,
    Duration,
    Laps,
    Bookmarks,
    /// <summary>Each lap's timestamp, joined with "; ".</summary>
    LapTimes,
    /// <summary>Each bookmark's timestamp and note, joined with "; ".</summary>
    BookmarkDetails,
    Notes,
    Snoozes,
    Acknowledgements,
    /// <summary>Per-event rows only: the event's type. Empty in per-timer rows.</summary>
    EventType,
    /// <summary>Per-event rows only: the event's UTC time.</summary>
    EventUtc,
    /// <summary>Per-event rows only: the event's note (a bookmark's text, a snooze length).</summary>
    EventNote,
}

/// <summary>One CSV column. Column order is its position in <see cref="ExportProfile.Columns"/>.</summary>
public sealed class ExportColumn
{
    public ExportSource Source { get; set; }
    public string Header { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional override of the profile default. Date sources and lap/bookmark lists: "iso" or a pattern such as
    /// <c>yyyy-MM-dd HH:mm</c>. Duration: <c>hh:mm:ss</c>, <c>decimal-hours</c> or <c>minutes</c>. Ignored elsewhere.
    /// </summary>
    public string? Format { get; set; }

    public ExportColumn() { }

    public ExportColumn(ExportSource source, string header, string? format = null)
    {
        Source = source;
        Header = header;
        Format = format;
    }
}

public sealed class ExportProfile : ICatalogItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool BuiltIn { get; set; }

    /// <summary>Field separator. Any non-empty text without quotes or line breaks, usually "," ";" or a tab.</summary>
    public string Delimiter { get; set; } = ",";
    public CsvQuoteStyle QuoteStyle { get; set; } = CsvQuoteStyle.Minimal;
    public CsvEncodingKind Encoding { get; set; } = CsvEncodingKind.Utf8Bom;
    public ExportRowMode RowMode { get; set; } = ExportRowMode.PerTimer;
    public bool IncludeHeader { get; set; } = true;

    /// <summary>"iso" (ISO 8601, UTC, e.g. 2026-10-14T09:30:00Z) or a custom pattern applied to the UTC time.</summary>
    public string DateFormat { get; set; } = ExportFormats.Iso;
    public DurationFormat DurationFormat { get; set; } = DurationFormat.HhMmSs;

    /// <summary>
    /// Prefix free text that starts with = + - @ with an apostrophe so a spreadsheet cannot run it as a formula.
    /// </summary>
    public bool ProtectFormulas { get; set; } = true;

    public List<ExportColumn> Columns { get; set; } = new();

    public override string ToString() => Name;

    public ExportProfile Clone() => new()
    {
        Id = Id, Name = Name, BuiltIn = BuiltIn, Delimiter = Delimiter, QuoteStyle = QuoteStyle, Encoding = Encoding,
        RowMode = RowMode, IncludeHeader = IncludeHeader, DateFormat = DateFormat, DurationFormat = DurationFormat,
        ProtectFormulas = ProtectFormulas,
        Columns = Columns.Select(c => new ExportColumn(c.Source, c.Header, c.Format) { Enabled = c.Enabled }).ToList(),
    };
}

/// <summary>Root of <c>exportprofiles.json</c>.</summary>
public sealed class ExportProfilesFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<ExportProfile> Profiles { get; set; } = BuiltInExportProfiles.Create();
}

public static class BuiltInExportProfiles
{
    public const string GenericId = "generic";
    public const string PsaId = "psa-ready";

    public static List<ExportProfile> Create() => new()
    {
        new ExportProfile
        {
            Id = GenericId, Name = "Generic", BuiltIn = true,
            Columns =
            {
                new(ExportSource.Name, "Name"),
                new(ExportSource.Ticket, "Ticket"),
                new(ExportSource.Client, "Client"),
                new(ExportSource.Priority, "Priority"),
                new(ExportSource.Type, "Type"),
                new(ExportSource.StartUtc, "Start (UTC)"),
                new(ExportSource.EndUtc, "End (UTC)"),
                new(ExportSource.Duration, "Duration"),
                new(ExportSource.Laps, "Laps"),
                new(ExportSource.Bookmarks, "Bookmarks"),
                new(ExportSource.LapTimes, "Lap times (UTC)"),
                new(ExportSource.BookmarkDetails, "Bookmark details"),
                new(ExportSource.Snoozes, "Snoozes"),
                new(ExportSource.Acknowledgements, "Acknowledgements"),
                new(ExportSource.Notes, "Notes"),
            },
        },
        // Placeholder layout until the PSA's import format is known (see GUIDE section 16). Remap by editing the profile.
        new ExportProfile
        {
            Id = PsaId, Name = "PSA-ready", BuiltIn = true,
            DateFormat = "yyyy-MM-dd HH:mm",
            DurationFormat = DurationFormat.DecimalHours,
            Columns =
            {
                new(ExportSource.Ticket, "Ticket Number"),
                new(ExportSource.Client, "Company"),
                new(ExportSource.Name, "Summary"),
                new(ExportSource.StartUtc, "Start Time"),
                new(ExportSource.EndUtc, "End Time"),
                new(ExportSource.Duration, "Hours"),
                new(ExportSource.Notes, "Notes"),
            },
        },
    };
}

public static class ExportFormats
{
    public const string Iso = "iso";
    public const string IsoPattern = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static bool TryParseDuration(string? text, out DurationFormat format)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "hh:mm:ss": format = DurationFormat.HhMmSs; return true;
            case "decimal-hours": format = DurationFormat.DecimalHours; return true;
            case "minutes": format = DurationFormat.Minutes; return true;
            default: format = default; return false;
        }
    }

    public static string DurationToken(DurationFormat format) => format switch
    {
        DurationFormat.DecimalHours => "decimal-hours",
        DurationFormat.Minutes => "minutes",
        _ => "hh:mm:ss",
    };

    /// <summary>True when <paramref name="pattern"/> is "iso" or a pattern .NET can format a date with.</summary>
    public static bool IsValidDatePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        if (pattern.Equals(Iso, StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            _ = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).ToString(pattern, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool IsDateSource(ExportSource s) =>
        s is ExportSource.StartUtc or ExportSource.EndUtc or ExportSource.EventUtc or ExportSource.LapTimes or ExportSource.BookmarkDetails;
}

using System.Text;
using Ticktower.Core.Engine;
using Ticktower.Core.Export;
using Ticktower.Core.Models;
using Ticktower.Core.Storage;

namespace Ticktower.Tests;

public sealed class CsvExportTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string> { ["interval-15"] = "15 min" };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ticktower-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static HistoryRecord Countdown(string name = "Call back", string ticket = "INC1", string client = "Acme", string notes = "")
    {
        var t = new TimerItem { Name = name, Ticket = ticket, Client = client, Notes = notes, Priority = Priority.P1, Kind = TimerKind.Countdown, Duration = TimeSpan.FromMinutes(15), TypeId = "interval-15" };
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Expire(t, T0.AddMinutes(16));
        TimerStateMachine.Acknowledge(t, T0.AddMinutes(20));
        return new HistoryRecord { ClosedUtc = T0.AddMinutes(30), Timer = t };
    }

    private static HistoryRecord Stopwatch()
    {
        var t = new TimerItem { Name = "Task", Kind = TimerKind.Stopwatch };
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Lap(t, T0.AddMinutes(10));
        TimerStateMachine.Bookmark(t, T0.AddMinutes(20), "found it");
        TimerStateMachine.Bookmark(t, T0.AddMinutes(25));
        TimerStateMachine.Pause(t, T0.AddMinutes(30));
        TimerStateMachine.Resume(t, T0.AddMinutes(40));            // 10 min paused
        TimerStateMachine.Stop(t, T0.AddMinutes(70));              // 30 + 30 = 60 min worked
        return new HistoryRecord { ClosedUtc = T0.AddMinutes(80), Timer = t };
    }

    private static ExportProfile Profile(Action<ExportProfile>? tweak = null)
    {
        var p = BuiltInExportProfiles.Create()[0];
        tweak?.Invoke(p);
        return p;
    }

    [Fact]
    public void Built_in_profiles_validate_and_have_the_expected_shape()
    {
        var all = BuiltInExportProfiles.Create();
        Assert.Equal(new[] { "generic", "psa-ready" }, all.Select(p => p.Id));
        Assert.All(all, p => { Assert.Empty(CsvExporter.Validate(p)); Assert.True(p.BuiltIn); });
        Assert.Equal(CsvEncodingKind.Utf8Bom, all[0].Encoding);
        Assert.Contains(all[0].Columns, c => c.Source == ExportSource.Duration);
    }

    [Fact]
    public void Generic_per_timer_row_for_a_countdown()
    {
        var table = CsvExporter.Build(Profile(), new[] { Countdown() }, Types);
        var csv = CsvExporter.ToCsv(Profile(), table).Split("\r\n");

        Assert.Equal("Name,Ticket,Client,Priority,Type,Start (UTC),End (UTC),Duration,Laps,Bookmarks,Lap times (UTC),Bookmark details,Snoozes,Acknowledgements,Notes", csv[0]);
        // Ended at the acknowledge (+20 min), not when the history record was closed (+30).
        Assert.Equal("Call back,INC1,Acme,P1,15 min,2026-10-14T09:00:00Z,2026-10-14T09:20:00Z,00:20:00,0,0,,,0,1,", csv[1]);
        Assert.Equal("", csv[2]);
    }

    [Fact]
    public void Stopwatch_duration_excludes_pauses_and_lists_laps_and_bookmarks_in_their_own_columns()
    {
        var profile = Profile(p => p.DateFormat = "HH:mm");
        var row = CsvExporter.Build(profile, new[] { Stopwatch() }, Types).Rows[0];
        string Col(ExportSource s) => row[profile.Columns.FindIndex(c => c.Source == s)].Text;

        Assert.Equal("01:00:00", Col(ExportSource.Duration));
        Assert.Equal("09:10", Col(ExportSource.LapTimes));
        Assert.Equal("09:20 found it; 09:25", Col(ExportSource.BookmarkDetails));
        Assert.Equal("1", Col(ExportSource.Laps));
        Assert.Equal("2", Col(ExportSource.Bookmarks));
        Assert.Equal("10:10", Col(ExportSource.EndUtc));    // the stop, not the +80 min close
    }

    [Fact]
    public void Duration_formats()
    {
        var span = TimeSpan.FromSeconds(9015); // 2h 30m 15s
        Assert.Equal("02:30:15", CsvExporter.FormatDuration(span, DurationFormat.HhMmSs));
        Assert.Equal("2.50", CsvExporter.FormatDuration(span, DurationFormat.DecimalHours));
        Assert.Equal("150.25", CsvExporter.FormatDuration(span, DurationFormat.Minutes));
        Assert.Equal("26:00:00", CsvExporter.FormatDuration(TimeSpan.FromHours(26), DurationFormat.HhMmSs));
        Assert.Equal("00:00:00", CsvExporter.FormatDuration(TimeSpan.FromSeconds(-5), DurationFormat.HhMmSs));
    }

    [Fact]
    public void Column_format_overrides_the_profile_default()
    {
        var profile = Profile(p =>
        {
            p.Columns = new()
            {
                new(ExportSource.StartUtc, "Day", "dd/MM/yyyy"),
                new(ExportSource.EndUtc, "End"),
                new(ExportSource.Duration, "Mins", "minutes"),
                new(ExportSource.Duration, "Hrs", "decimal-hours"),
                new(ExportSource.Duration, "Default"),
            };
        });
        var row = CsvExporter.Build(profile, new[] { Countdown() }, Types).Rows[0].Select(c => c.Text).ToArray();
        Assert.Equal(new[] { "14/10/2026", "2026-10-14T09:20:00Z", "20.00", "0.33", "00:20:00" }, row);
    }

    [Fact]
    public void Disabled_columns_are_left_out_and_order_follows_the_list()
    {
        var profile = Profile(p =>
        {
            p.Columns = new() { new(ExportSource.Ticket, "T"), new(ExportSource.Name, "N") { Enabled = false }, new(ExportSource.Client, "C") };
            p.Columns.Reverse();
        });
        var table = CsvExporter.Build(profile, new[] { Countdown() }, Types);
        Assert.Equal(new[] { "C", "T" }, table.Headers);
        Assert.Equal(new[] { "Acme", "INC1" }, table.Rows[0].Select(c => c.Text));
    }

    [Fact]
    public void Per_event_mode_writes_one_row_per_event_and_keeps_timer_fields()
    {
        var profile = Profile(p =>
        {
            p.RowMode = ExportRowMode.PerEvent;
            p.Columns = new() { new(ExportSource.Name, "Name"), new(ExportSource.EventType, "Event"), new(ExportSource.EventUtc, "When"), new(ExportSource.EventNote, "Note") };
        });
        var table = CsvExporter.Build(profile, new[] { Stopwatch() }, Types);

        Assert.Equal(7, table.Rows.Count);   // start, lap, 2 bookmarks, pause, resume, stop
        var events = table.Rows.Select(r => r[1].Text).ToList();
        Assert.Equal(new[] { "Start", "Lap", "Bookmark", "Bookmark", "Pause", "Resume", "Stop" }, events);
        Assert.All(table.Rows, r => Assert.Equal("Task", r[0].Text));
        var bookmark = table.Rows[2];
        Assert.Equal("found it", bookmark[3].Text);
        Assert.Equal("2026-10-14T09:20:00Z", bookmark[2].Text);
    }

    [Fact]
    public void Event_columns_are_empty_in_per_timer_mode()
    {
        var profile = Profile(p => p.Columns = new() { new(ExportSource.EventType, "E"), new(ExportSource.EventUtc, "U"), new(ExportSource.EventNote, "N") });
        Assert.All(CsvExporter.Build(profile, new[] { Countdown() }, Types).Rows[0], c => Assert.Equal("", c.Text));
    }

    [Fact]
    public void A_timer_with_no_events_still_gets_one_row_in_per_event_mode()
    {
        var pending = new HistoryRecord { ClosedUtc = T0, Timer = new TimerItem { Name = "queued", State = TimerState.Pending } };
        var profile = Profile(p => { p.RowMode = ExportRowMode.PerEvent; p.Columns = new() { new(ExportSource.Name, "N"), new(ExportSource.StartUtc, "S"), new(ExportSource.Duration, "D") }; });
        var row = Assert.Single(CsvExporter.Build(profile, new[] { pending }, Types).Rows);
        Assert.Equal(new[] { "queued", "", "" }, row.Select(c => c.Text));
    }

    [Fact]
    public void Minimal_quoting_quotes_only_when_needed_and_doubles_quotes()
    {
        var rec = Countdown(name: "Reset \"VPN\", urgent", notes: "line1\nline2");
        var profile = Profile(p => p.Columns = new() { new(ExportSource.Name, "Name"), new(ExportSource.Notes, "Notes"), new(ExportSource.Ticket, "Ticket") });
        var csv = CsvExporter.ToCsv(profile, CsvExporter.Build(profile, new[] { rec }, Types));
        Assert.Equal("Name,Notes,Ticket\r\n\"Reset \"\"VPN\"\", urgent\",\"line1\nline2\",INC1\r\n", csv);
    }

    [Fact]
    public void Delimiter_is_honoured_and_a_field_containing_it_is_quoted()
    {
        var profile = Profile(p =>
        {
            p.Delimiter = ";";
            p.Columns = new() { new(ExportSource.Name, "Name"), new(ExportSource.Client, "Client") };
        });
        var csv = CsvExporter.ToCsv(profile, CsvExporter.Build(profile, new[] { Countdown(name: "a;b", client: "x,y") }, Types));
        Assert.Equal("Name;Client\r\n\"a;b\";x,y\r\n", csv);
    }

    [Fact]
    public void Quote_all_and_non_numeric_styles()
    {
        var rec = Countdown(ticket: "12345");
        List<ExportColumn> Cols() => new() { new(ExportSource.Ticket, "Ticket"), new(ExportSource.Laps, "Laps"), new(ExportSource.Priority, "Pri") };

        var all = Profile(p => { p.QuoteStyle = CsvQuoteStyle.All; p.Columns = Cols(); });
        Assert.Equal("\"Ticket\",\"Laps\",\"Pri\"\r\n\"12345\",\"0\",\"P1\"\r\n", CsvExporter.ToCsv(all, CsvExporter.Build(all, new[] { rec }, Types)));

        var nn = Profile(p => { p.QuoteStyle = CsvQuoteStyle.NonNumeric; p.Columns = Cols(); });
        // The ticket is typed text, so it stays quoted even though it looks like a number; generated counts do not.
        Assert.Equal("\"Ticket\",\"Laps\",\"Pri\"\r\n\"12345\",0,\"P1\"\r\n", CsvExporter.ToCsv(nn, CsvExporter.Build(nn, new[] { rec }, Types)));
    }

    [Fact]
    public void Header_row_can_be_turned_off()
    {
        var profile = Profile(p => { p.IncludeHeader = false; p.Columns = new() { new(ExportSource.Ticket, "Ticket") }; });
        Assert.Equal("INC1\r\n", CsvExporter.ToCsv(profile, CsvExporter.Build(profile, new[] { Countdown() }, Types)));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", true)]
    [InlineData("+1", true)]
    [InlineData("-2", true)]
    [InlineData("@SUM(A1)", true)]
    [InlineData("=cmd", false)]
    public void Formula_like_text_is_defused_unless_switched_off(string name, bool protect)
    {
        var profile = Profile(p => { p.ProtectFormulas = protect; p.Columns = new() { new(ExportSource.Name, "N") }; });
        var text = CsvExporter.Build(profile, new[] { Countdown(name: name) }, Types).Rows[0][0].Text;
        var csv = CsvExporter.ToCsv(profile, CsvExporter.Build(profile, new[] { Countdown(name: name) }, Types));
        Assert.Equal(name, text);                                   // the table holds the real value
        Assert.Equal(protect, csv.Split("\r\n")[1].TrimStart('"').StartsWith("'"));
    }

    [Fact]
    public void Generated_dates_are_never_defused()
    {
        var profile = Profile(p => p.Columns = new() { new(ExportSource.StartUtc, "S") });
        var csv = CsvExporter.ToCsv(profile, CsvExporter.Build(profile, new[] { Countdown() }, Types));
        Assert.Equal("S\r\n2026-10-14T09:00:00Z\r\n", csv);
    }

    [Fact]
    public void Type_id_falls_back_when_the_type_was_deleted()
    {
        var profile = Profile(p => p.Columns = new() { new(ExportSource.Type, "T") });
        var rec = Countdown();
        rec.Timer.TypeId = "gone";
        Assert.Equal("gone", CsvExporter.Build(profile, new[] { rec }, Types).Rows[0][0].Text);
    }

    [Fact]
    public void Validate_reports_each_problem()
    {
        var p = new ExportProfile { Delimiter = "", DateFormat = "%%%%x", Columns = new() };
        var errors = CsvExporter.Validate(p);
        Assert.Equal(3, errors.Count);

        var bad = Profile(x => { x.Delimiter = "\""; x.Columns[7].Format = "weeks"; x.Columns[5].Format = "yyyy-MM-dd\\"; });
        Assert.Equal(3, CsvExporter.Validate(bad).Count);
        Assert.Throws<InvalidOperationException>(() => CsvExporter.Build(bad, Array.Empty<HistoryRecord>(), Types));
    }

    [Theory]
    [InlineData(CsvEncodingKind.Utf8Bom, new byte[] { 0xEF, 0xBB, 0xBF })]
    [InlineData(CsvEncodingKind.Utf8, new byte[] { (byte)'N' })]
    [InlineData(CsvEncodingKind.Utf16, new byte[] { 0xFF, 0xFE })]
    public void Written_file_carries_the_chosen_encoding_marker(CsvEncodingKind kind, byte[] prefix)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "out.csv");
        var profile = Profile(p => { p.Encoding = kind; p.Columns = new() { new(ExportSource.Name, "N") }; });
        CsvExporter.WriteFile(path, profile, CsvExporter.Build(profile, new[] { Countdown() }, Types));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(prefix, bytes.Take(prefix.Length));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Latin1_and_utf8_round_trip_accents_and_latin1_replaces_what_it_cannot_hold()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "out.csv");
        var rec = Countdown(name: "Café ✓");
        var profile = Profile(p => { p.Encoding = CsvEncodingKind.Utf8; p.IncludeHeader = false; p.Columns = new() { new(ExportSource.Name, "N") }; });
        CsvExporter.WriteFile(path, profile, CsvExporter.Build(profile, new[] { rec }, Types));
        Assert.Equal("Café ✓\r\n", File.ReadAllText(path, Encoding.UTF8));

        profile.Encoding = CsvEncodingKind.Latin1;
        CsvExporter.WriteFile(path, profile, CsvExporter.Build(profile, new[] { rec }, Types));
        Assert.Equal("Café ?\r\n", File.ReadAllText(path, Encoding.Latin1));
    }

    [Fact]
    public void WriteFile_replaces_an_existing_export()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "out.csv");
        File.WriteAllText(path, "old");
        var profile = Profile(p => { p.Encoding = CsvEncodingKind.Utf8; p.Columns = new() { new(ExportSource.Ticket, "T") }; });
        CsvExporter.WriteFile(path, profile, CsvExporter.Build(profile, new[] { Countdown() }, Types));
        Assert.StartsWith("T\r\nINC1", File.ReadAllText(path));
    }

    [Fact]
    public void ReadRange_spans_months_filters_by_close_time_and_sorts()
    {
        var writer = new HistoryWriter(_dir);
        var a = Countdown(name: "oct-late");  writer.Append(a.Timer, new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc));
        var b = Countdown(name: "nov-early"); writer.Append(b.Timer, new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc));
        var c = Countdown(name: "sep");       writer.Append(c.Timer, new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc));
        var d = Countdown(name: "nov-late");  writer.Append(d.Timer, new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));

        var range = writer.ReadRange(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new[] { "oct-late", "nov-early" }, range.Select(r => r.Timer.Name));

        Assert.Empty(writer.ReadRange(T0, T0));
        // The end is exclusive.
        var exact = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.Empty(writer.ReadRange(exact.AddDays(-1), exact));
    }

    [Fact]
    public void Profiles_file_round_trips_and_a_profile_survives_catalog_export_and_import()
    {
        var store = new JsonFileStore();
        var path = Path.Combine(_dir, "exportprofiles.json");
        var custom = Profile(p => { p.Id = "mine"; p.Name = "Mine"; p.BuiltIn = false; p.Delimiter = "\t"; p.QuoteStyle = CsvQuoteStyle.All; p.RowMode = ExportRowMode.PerEvent; p.Columns[7].Format = "minutes"; });
        store.Save(path, new ExportProfilesFile { Profiles = { custom } });
        var loaded = store.Load(path, () => new ExportProfilesFile());
        Assert.Equal(LoadOutcome.Loaded, loaded.Outcome);

        var export = Path.Combine(_dir, "share.json");
        CatalogIO.Export(export, "exportprofiles", loaded.Value.Profiles.Where(p => !p.BuiltIn));
        var existing = BuiltInExportProfiles.Create();
        var result = CatalogIO.Merge(existing, CatalogIO.Read<ExportProfile>(export, "exportprofiles"));

        Assert.Equal(new MergeResult(1, 0), result);
        var back = existing.Single(p => p.Id == "mine");
        Assert.Equal("\t", back.Delimiter);
        Assert.Equal(CsvQuoteStyle.All, back.QuoteStyle);
        Assert.Equal(ExportRowMode.PerEvent, back.RowMode);
        Assert.Equal("minutes", back.Columns[7].Format);
        Assert.Equal(custom.Columns.Count, back.Columns.Count);
    }

    [Fact]
    public void Clone_is_independent()
    {
        var a = Profile();
        var b = a.Clone();
        b.Columns[0].Header = "changed";
        b.Delimiter = ";";
        Assert.Equal("Name", a.Columns[0].Header);
        Assert.Equal(",", a.Delimiter);
    }
}

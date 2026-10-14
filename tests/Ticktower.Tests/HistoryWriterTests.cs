using System.Text.Json;
using Ticktower.Core.Engine;
using Ticktower.Core.Models;
using Ticktower.Core.Storage;

namespace Ticktower.Tests;

public sealed class HistoryWriterTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 31, 23, 50, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ticktower-tests-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = T0;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed class NullSink : IAlertSink
    {
        public void Alert(AlertNotice n) { }
    }

    private static TimerItem Countdown(string name) =>
        new() { Name = name, Kind = TimerKind.Countdown, Duration = TimeSpan.FromMinutes(5) };

    private string[] Lines(string file) => File.ReadAllLines(Path.Combine(_dir, "history", file));

    [Fact]
    public void Append_writes_one_json_line_in_the_month_file_of_the_close_time()
    {
        var writer = new HistoryWriter(_dir);
        var t = Countdown("a");
        TimerStateMachine.Start(t, T0);

        writer.Append(t, T0.AddMinutes(20)); // crosses into November

        Assert.False(File.Exists(Path.Combine(_dir, "history", "2026-10.jsonl")));
        var lines = Lines("2026-11.jsonl");
        Assert.Single(lines);
        var round = JsonSerializer.Deserialize<HistoryRecord>(lines[0], JsonFileStore.Options)!;
        Assert.Equal(HistoryRecord.CurrentVersion, round.Version);
        Assert.Equal(T0.AddMinutes(20), round.ClosedUtc);
        Assert.Equal("a", round.Timer.Name);
        Assert.Equal(TimerEventType.Start, round.Timer.Events[0].Type);
    }

    [Fact]
    public void Append_is_single_line_even_with_newlines_in_notes()
    {
        var writer = new HistoryWriter(_dir);
        var t = Countdown("a");
        t.Notes = "line1\nline2";
        TimerStateMachine.Start(t, T0);

        writer.Append(t, T0);
        writer.Append(t, T0);

        Assert.Equal(2, Lines("2026-10.jsonl").Length);
    }

    [Fact]
    public void ReadMonth_returns_records_in_append_order_and_empty_when_missing()
    {
        var writer = new HistoryWriter(_dir);
        Assert.Empty(writer.ReadMonth(2026, 10));

        var a = Countdown("a");
        var b = Countdown("b");
        TimerStateMachine.Start(a, T0);
        TimerStateMachine.Start(b, T0);
        writer.Append(a, T0);
        writer.Append(b, T0.AddMinutes(1));

        Assert.Equal(new[] { "a", "b" }, writer.ReadMonth(2026, 10).Select(r => r.Timer.Name));
    }

    [Fact]
    public void ReadMonth_skips_a_corrupt_line_without_losing_the_rest()
    {
        var writer = new HistoryWriter(_dir);
        var a = Countdown("a");
        TimerStateMachine.Start(a, T0);
        writer.Append(a, T0);
        File.AppendAllText(Path.Combine(_dir, "history", "2026-10.jsonl"), "{not json\n");
        writer.Append(a, T0);

        Assert.Equal(2, writer.ReadMonth(2026, 10).Count);
    }

    [Fact]
    public void Engine_Remove_raises_Closed_with_the_timer_and_time()
    {
        var engine = new TimerEngine(new NullSink(), () => _now);
        var t = engine.Add(Countdown("a"));
        _now = T0.AddMinutes(2);
        TimerItem? closed = null;
        DateTime closedAt = default;
        engine.Closed += (timer, utc) => { closed = timer; closedAt = utc; };

        Assert.True(engine.Remove(t.Id));

        Assert.Same(t, closed);
        Assert.Equal(_now, closedAt);
    }

    [Fact]
    public void Engine_Edit_applies_the_change_and_raises_Changed()
    {
        var engine = new TimerEngine(new NullSink(), () => _now);
        var t = engine.Add(Countdown("a"));
        var changed = 0;
        engine.Changed += () => changed++;

        engine.Edit(t.Id, x => x.Ticket = "INC9");

        Assert.Equal("INC9", t.Ticket);
        Assert.Equal(1, changed);
        Assert.Throws<KeyNotFoundException>(() => engine.Edit(Guid.NewGuid(), _ => { }));
    }

    [Fact]
    public void Engine_Remove_of_unknown_id_does_not_raise_Closed()
    {
        var engine = new TimerEngine(new NullSink(), () => _now);
        var raised = false;
        engine.Closed += (_, _) => raised = true;

        Assert.False(engine.Remove(Guid.NewGuid()));
        Assert.False(raised);
    }
}

using Ticktower.Core.Engine;
using Ticktower.Core.Models;
using Ticktower.Core.Storage;

namespace Ticktower.Tests;

public sealed class TimerEngineTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ticktower-tests-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = T0;
    private readonly RecordingSink _sink = new();

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private TimerEngine NewEngine() => new(_sink, () => _now);

    private static TimerItem Countdown(string name, int minutes) =>
        new() { Name = name, Kind = TimerKind.Countdown, Duration = TimeSpan.FromMinutes(minutes) };

    private sealed class RecordingSink : IAlertSink
    {
        public List<(string Name, bool Missed)> Fired { get; } = new();
        public void Expired(TimerItem timer, bool missed) => Fired.Add((timer.Name, missed));
    }

    [Fact]
    public void Add_starts_the_timer_and_raises_Changed()
    {
        var engine = NewEngine();
        var changed = 0;
        engine.Changed += () => changed++;

        var t = engine.Add(Countdown("a", 5));
        Assert.Equal(T0.AddMinutes(5), t.EndUtc);
        Assert.Single(engine.Timers);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Tick_before_due_does_nothing()
    {
        var engine = NewEngine();
        engine.Add(Countdown("a", 5));
        _now = T0.AddMinutes(4);
        Assert.Empty(engine.Tick());
        Assert.Empty(_sink.Fired);
    }

    [Fact]
    public void Tick_expires_only_due_timers_once_and_logs_alert_fired()
    {
        var engine = NewEngine();
        var a = engine.Add(Countdown("a", 5));
        engine.Add(Countdown("b", 30));

        _now = T0.AddMinutes(6);
        var expired = engine.Tick();

        Assert.Same(a, Assert.Single(expired));
        Assert.Equal(new[] { ("a", false) }, _sink.Fired);
        Assert.Equal(TimerEventType.AlertFired, a.Events.Last().Type);

        _now = T0.AddMinutes(7);
        Assert.Empty(engine.Tick());
        Assert.Single(_sink.Fired);
    }

    [Fact]
    public void Stopwatch_never_expires()
    {
        var engine = NewEngine();
        engine.Add(new TimerItem { Name = "sw", Kind = TimerKind.Stopwatch });
        _now = T0.AddDays(3);
        Assert.Empty(engine.Tick());
    }

    [Fact]
    public void Recompute_fires_alerts_missed_while_app_was_not_running()
    {
        var first = NewEngine();
        first.Add(Countdown("slept", 5));
        first.Add(Countdown("future", 120));
        var snapshot = first.Snapshot();

        _now = T0.AddMinutes(45); // PC slept / app was killed
        var second = NewEngine();
        second.Load(snapshot.Timers);
        var expired = second.Recompute();

        Assert.Equal("slept", Assert.Single(expired).Name);
        Assert.Equal(new[] { ("slept", true) }, _sink.Fired);
        Assert.Equal(TimerState.Expired, second.Timers.Single(x => x.Name == "slept").State);
        Assert.Equal(TimerState.Running, second.Timers.Single(x => x.Name == "future").State);
        var expireEvent = second.Timers.Single(x => x.Name == "slept").Events.Single(e => e.Type == TimerEventType.Expire);
        Assert.Equal(T0.AddMinutes(5), expireEvent.Utc);
    }

    [Fact]
    public void Already_expired_timer_is_not_alerted_again_after_reload()
    {
        var first = NewEngine();
        first.Add(Countdown("a", 5));
        _now = T0.AddMinutes(6);
        first.Tick();
        _sink.Fired.Clear();

        var second = NewEngine();
        second.Load(first.Snapshot().Timers);
        Assert.Empty(second.Recompute());
        Assert.Empty(_sink.Fired);
    }

    [Fact]
    public void State_survives_a_save_and_relaunch_through_the_store()
    {
        var path = Path.Combine(_dir, "state.json");
        var store = new JsonFileStore();

        var first = NewEngine();
        first.Add(Countdown("cd", 60));
        first.Add(new TimerItem { Name = "sw", Kind = TimerKind.Stopwatch });
        first.Add(new TimerItem { Name = "iv", Kind = TimerKind.Interval, Duration = TimeSpan.FromMinutes(15) });
        _now = T0.AddMinutes(20);
        first.Tick();
        store.Save(path, first.Snapshot());

        // "kill" the app, relaunch 10 minutes later
        _now = T0.AddMinutes(30);
        var loaded = store.Load(path, () => new StateFile()).Value;
        var second = NewEngine();
        second.Load(loaded.Timers);
        second.Recompute();

        Assert.Equal(3, second.Timers.Count);
        var sw = second.Timers.Single(x => x.Name == "sw");
        Assert.Equal(TimeSpan.FromMinutes(30), TimerStateMachine.Elapsed(sw, _now));
        Assert.Equal(TimerState.Expired, second.Timers.Single(x => x.Name == "iv").State);
        Assert.Equal(TimerState.Running, second.Timers.Single(x => x.Name == "cd").State);
    }

    [Fact]
    public void Acknowledge_and_Snooze_work_by_id_and_raise_Changed()
    {
        var engine = NewEngine();
        var a = engine.Add(Countdown("a", 1));
        var b = engine.Add(Countdown("b", 1));
        _now = T0.AddMinutes(2);
        engine.Tick();

        var changed = 0;
        engine.Changed += () => changed++;
        engine.Acknowledge(a.Id);
        engine.Snooze(b.Id, TimeSpan.FromMinutes(5));

        Assert.Equal(TimerState.Acknowledged, a.State);
        Assert.Equal(TimerState.Running, b.State);
        Assert.Equal(_now.AddMinutes(5), b.EndUtc);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Stopwatch_actions_by_id()
    {
        var engine = NewEngine();
        var sw = engine.Add(new TimerItem { Name = "sw", Kind = TimerKind.Stopwatch });
        _now = T0.AddMinutes(1);
        engine.Lap(sw.Id);
        engine.Bookmark(sw.Id, "called");
        engine.Pause(sw.Id);
        _now = T0.AddMinutes(5);
        engine.Resume(sw.Id);
        _now = T0.AddMinutes(6);
        engine.Stop(sw.Id);

        Assert.Equal(TimerState.Stopped, sw.State);
        Assert.Equal(TimeSpan.FromMinutes(2), TimerStateMachine.Elapsed(sw, _now));
    }

    [Fact]
    public void Failed_action_does_not_raise_Changed()
    {
        var engine = NewEngine();
        var a = engine.Add(Countdown("a", 5));
        var changed = 0;
        engine.Changed += () => changed++;

        Assert.Throws<InvalidOperationException>(() => engine.Acknowledge(a.Id));
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Unknown_id_throws()
    {
        var engine = NewEngine();
        Assert.Throws<KeyNotFoundException>(() => engine.Acknowledge(Guid.NewGuid()));
    }

    [Fact]
    public void Remove_deletes_and_raises_Changed()
    {
        var engine = NewEngine();
        var a = engine.Add(Countdown("a", 5));
        var changed = 0;
        engine.Changed += () => changed++;

        Assert.True(engine.Remove(a.Id));
        Assert.Empty(engine.Timers);
        Assert.False(engine.Remove(a.Id));
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Non_utc_clock_is_rejected()
    {
        var engine = new TimerEngine(_sink, () => DateTime.Now);
        Assert.Throws<InvalidOperationException>(() => engine.Add(Countdown("a", 5)));
    }
}

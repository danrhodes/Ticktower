using Ticktower.Core.Engine;
using Ticktower.Core.Models;

namespace Ticktower.Tests;

public class ChainTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private DateTime _now = T0;

    private sealed class NullSink : IAlertSink
    {
        public void Alert(AlertNotice n) { }
    }

    private TimerEngine NewEngine() => new(new NullSink(), () => _now);

    private static TimerItem Countdown(string name, int minutes = 5) =>
        new() { Name = name, Kind = TimerKind.Countdown, Duration = TimeSpan.FromMinutes(minutes) };

    [Fact]
    public void AddChain_runs_first_and_queues_second()
    {
        var engine = NewEngine();
        var a = Countdown("a");
        var b = Countdown("b");
        engine.AddChain(a, b);

        Assert.Equal(TimerState.Running, a.State);
        Assert.Equal(TimerState.Pending, b.State);
        Assert.Equal(b.Id, a.NextTimerId);
        Assert.Empty(b.Events);
        Assert.Equal(Lane.Active, BoardLogic.LaneOf(b));
        Assert.Equal("Queued", BoardLogic.Card(b, _now).TimeText);
    }

    [Fact]
    public void Pending_timer_never_expires_or_ticks()
    {
        var engine = NewEngine();
        engine.AddChain(Countdown("a"), Countdown("b"));
        _now = T0.AddDays(1);
        engine.Tick();
        Assert.Equal(TimerState.Pending, engine.Timers[0].State);
    }

    [Fact]
    public void Acknowledging_a_countdown_starts_the_next_timer_at_that_moment()
    {
        var engine = NewEngine();
        var a = Countdown("a");
        var b = Countdown("b", 10);
        engine.AddChain(a, b);

        _now = T0.AddMinutes(6);
        engine.Tick();
        _now = T0.AddMinutes(7);
        engine.Acknowledge(a.Id);

        Assert.Equal(TimerState.Running, b.State);
        Assert.Equal(_now, b.StartedUtc);
        Assert.Equal(_now.AddMinutes(10), b.EndUtc);
    }

    [Fact]
    public void Stopping_a_stopwatch_starts_the_next_timer()
    {
        var engine = NewEngine();
        var sw = new TimerItem { Name = "sw", Kind = TimerKind.Stopwatch };
        var b = Countdown("b");
        engine.AddChain(sw, b);

        _now = T0.AddMinutes(3);
        engine.Stop(sw.Id);
        Assert.Equal(TimerState.Running, b.State);
    }

    [Fact]
    public void Pausing_does_not_complete_the_stopwatch()
    {
        var engine = NewEngine();
        var sw = new TimerItem { Name = "sw", Kind = TimerKind.Stopwatch };
        var b = Countdown("b");
        engine.AddChain(sw, b);
        engine.Pause(sw.Id);
        Assert.Equal(TimerState.Pending, b.State);
    }

    [Fact]
    public void Interval_acknowledge_starts_the_next_once_and_later_acks_leave_it_alone()
    {
        var engine = NewEngine();
        var a = new TimerItem { Name = "a", Kind = TimerKind.Interval, Duration = TimeSpan.FromMinutes(5) };
        var b = Countdown("b");
        engine.AddChain(a, b);

        _now = T0.AddMinutes(6);
        engine.Tick();
        engine.Acknowledge(a.Id);
        var started = b.StartedUtc;
        Assert.Equal(TimerState.Running, b.State);

        _now = T0.AddMinutes(12);
        engine.Tick();
        engine.Acknowledge(a.Id);
        Assert.Equal(started, b.StartedUtc);
    }

    [Fact]
    public void Missing_or_already_started_next_timer_is_ignored()
    {
        var engine = NewEngine();
        var a = Countdown("a");
        a.NextTimerId = Guid.NewGuid();
        engine.Add(a);
        _now = T0.AddMinutes(6);
        engine.Tick();
        engine.Acknowledge(a.Id);
        Assert.Equal(TimerState.Acknowledged, a.State);
    }

    [Fact]
    public void Pending_timer_can_be_started_manually_but_a_running_one_cannot()
    {
        var engine = NewEngine();
        var b = engine.AddPending(Countdown("b"));
        engine.Start(b.Id);
        Assert.Equal(TimerState.Running, b.State);
        Assert.Throws<InvalidOperationException>(() => engine.Start(b.Id));
    }
}

using Ticktower.Core.Engine;
using Ticktower.Core.Models;

namespace Ticktower.Tests;

public class TimerStateMachineTests
{
    private static readonly DateTime T0 = new(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);

    private static TimerItem Countdown(int minutes = 15) =>
        new() { Kind = TimerKind.Countdown, Duration = TimeSpan.FromMinutes(minutes) };

    private static TimerItem Interval(int minutes = 15) =>
        new() { Kind = TimerKind.Interval, Duration = TimeSpan.FromMinutes(minutes) };

    private static TimerItem Stopwatch() => new() { Kind = TimerKind.Stopwatch };

    private static TimerEventType[] Types(TimerItem t) => t.Events.Select(e => e.Type).ToArray();

    [Fact]
    public void Start_countdown_sets_end_and_logs_start()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        Assert.Equal(TimerState.Running, t.State);
        Assert.Equal(T0, t.StartedUtc);
        Assert.Equal(T0.AddMinutes(15), t.EndUtc);
        Assert.Equal(new[] { TimerEventType.Start }, Types(t));
    }

    [Fact]
    public void Start_twice_throws()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        Assert.Throws<InvalidOperationException>(() => TimerStateMachine.Start(t, T0));
    }

    [Fact]
    public void Start_countdown_without_duration_throws()
    {
        var t = new TimerItem { Kind = TimerKind.Countdown };
        Assert.Throws<InvalidOperationException>(() => TimerStateMachine.Start(t, T0));
    }

    [Fact]
    public void Expire_before_end_is_noop()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        Assert.False(TimerStateMachine.Expire(t, T0.AddMinutes(14)));
        Assert.Equal(TimerState.Running, t.State);
    }

    [Fact]
    public void Expire_logs_expire_at_end_time_even_when_late()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        Assert.True(TimerStateMachine.Expire(t, T0.AddMinutes(40)));
        Assert.Equal(TimerState.Expired, t.State);
        var ev = t.Events.Last();
        Assert.Equal(TimerEventType.Expire, ev.Type);
        Assert.Equal(T0.AddMinutes(15), ev.Utc);
    }

    [Fact]
    public void Expire_twice_is_noop()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Expire(t, T0.AddMinutes(16));
        Assert.False(TimerStateMachine.Expire(t, T0.AddMinutes(17)));
        Assert.Single(t.Events, e => e.Type == TimerEventType.Expire);
    }

    [Fact]
    public void Ack_countdown_goes_to_acknowledged()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Expire(t, T0.AddMinutes(16));
        TimerStateMachine.Acknowledge(t, T0.AddMinutes(17));
        Assert.Equal(TimerState.Acknowledged, t.State);
        Assert.Equal(T0.AddMinutes(17), t.LastAckUtc);
    }

    [Fact]
    public void Ack_running_countdown_throws()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        Assert.Throws<InvalidOperationException>(() => TimerStateMachine.Acknowledge(t, T0.AddMinutes(1)));
    }

    [Fact]
    public void Ack_interval_restarts_from_ack_time()
    {
        var t = Interval(15);
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Expire(t, T0.AddMinutes(20));
        TimerStateMachine.Acknowledge(t, T0.AddMinutes(20));
        Assert.Equal(TimerState.Running, t.State);
        Assert.Equal(T0.AddMinutes(35), t.EndUtc);
        Assert.Equal(T0.AddMinutes(20), t.LastAckUtc);
        Assert.Equal(
            new[] { TimerEventType.Start, TimerEventType.Expire, TimerEventType.Ack },
            Types(t));
    }

    [Fact]
    public void Snooze_expired_returns_to_running_with_new_end()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Expire(t, T0.AddMinutes(16));
        TimerStateMachine.Snooze(t, T0.AddMinutes(17), TimeSpan.FromMinutes(5));
        Assert.Equal(TimerState.Running, t.State);
        Assert.Equal(T0.AddMinutes(22), t.EndUtc);
        Assert.Equal(TimerEventType.Snooze, t.Events.Last().Type);
    }

    [Fact]
    public void Snooze_running_throws()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        Assert.Throws<InvalidOperationException>(
            () => TimerStateMachine.Snooze(t, T0, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Snooze_zero_throws()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Expire(t, T0.AddMinutes(16));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TimerStateMachine.Snooze(t, T0.AddMinutes(16), TimeSpan.Zero));
    }

    [Fact]
    public void Snoozed_interval_expires_again_and_ack_restarts_full_interval()
    {
        var t = Interval(10);
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Expire(t, T0.AddMinutes(10));
        TimerStateMachine.Snooze(t, T0.AddMinutes(10), TimeSpan.FromMinutes(2));
        Assert.True(TimerStateMachine.Expire(t, T0.AddMinutes(12)));
        TimerStateMachine.Acknowledge(t, T0.AddMinutes(13));
        Assert.Equal(T0.AddMinutes(23), t.EndUtc);
    }

    [Fact]
    public void Stopwatch_pause_resume_stop_tracks_elapsed()
    {
        var t = Stopwatch();
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Pause(t, T0.AddMinutes(10));
        Assert.Equal(TimerState.Paused, t.State);
        TimerStateMachine.Resume(t, T0.AddMinutes(30));
        TimerStateMachine.Stop(t, T0.AddMinutes(35));
        Assert.Equal(TimerState.Stopped, t.State);
        Assert.Equal(TimeSpan.FromMinutes(15), TimerStateMachine.Elapsed(t, T0.AddHours(5)));
    }

    [Fact]
    public void Elapsed_while_running_uses_now()
    {
        var t = Stopwatch();
        TimerStateMachine.Start(t, T0);
        Assert.Equal(TimeSpan.FromMinutes(7), TimerStateMachine.Elapsed(t, T0.AddMinutes(7)));
    }

    [Fact]
    public void Elapsed_while_paused_is_frozen()
    {
        var t = Stopwatch();
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Pause(t, T0.AddMinutes(4));
        Assert.Equal(TimeSpan.FromMinutes(4), TimerStateMachine.Elapsed(t, T0.AddHours(2)));
    }

    [Fact]
    public void Stop_while_paused_is_allowed_and_keeps_elapsed()
    {
        var t = Stopwatch();
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Pause(t, T0.AddMinutes(4));
        TimerStateMachine.Stop(t, T0.AddMinutes(9));
        Assert.Equal(TimerState.Stopped, t.State);
        Assert.Equal(TimeSpan.FromMinutes(4), TimerStateMachine.Elapsed(t, T0.AddHours(1)));
    }

    [Fact]
    public void Pause_on_stopped_stopwatch_or_countdown_throws()
    {
        var sw = Stopwatch();
        TimerStateMachine.Start(sw, T0);
        TimerStateMachine.Stop(sw, T0.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => TimerStateMachine.Pause(sw, T0.AddMinutes(2)));

        var cd = Countdown();
        TimerStateMachine.Start(cd, T0);
        Assert.Throws<InvalidOperationException>(() => TimerStateMachine.Pause(cd, T0.AddMinutes(2)));
    }

    [Fact]
    public void Resume_when_running_throws()
    {
        var t = Stopwatch();
        TimerStateMachine.Start(t, T0);
        Assert.Throws<InvalidOperationException>(() => TimerStateMachine.Resume(t, T0.AddMinutes(1)));
    }

    [Fact]
    public void Lap_only_while_stopwatch_running()
    {
        var t = Stopwatch();
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Lap(t, T0.AddMinutes(1));
        TimerStateMachine.Pause(t, T0.AddMinutes(2));
        Assert.Throws<InvalidOperationException>(() => TimerStateMachine.Lap(t, T0.AddMinutes(3)));
        Assert.Equal(1, TimerStateMachine.LapCount(t));
    }

    [Fact]
    public void Bookmark_stores_note_and_counts()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Bookmark(t, T0.AddMinutes(1), "rang client");
        TimerStateMachine.Bookmark(t, T0.AddMinutes(2));
        Assert.Equal(2, TimerStateMachine.BookmarkCount(t));
        Assert.Equal("rang client", t.Events.First(e => e.Type == TimerEventType.Bookmark).Note);
    }

    [Fact]
    public void Stop_on_countdown_throws()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        Assert.Throws<InvalidOperationException>(() => TimerStateMachine.Stop(t, T0.AddMinutes(1)));
    }

    [Fact]
    public void Non_utc_times_are_rejected()
    {
        var t = Countdown();
        var local = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Local);
        Assert.Throws<ArgumentException>(() => TimerStateMachine.Start(t, local));
    }

    [Fact]
    public void RecordAlertFired_appends_event()
    {
        var t = Countdown();
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Expire(t, T0.AddMinutes(16));
        TimerStateMachine.RecordAlertFired(t, T0.AddMinutes(16), "toast");
        Assert.Equal(TimerEventType.AlertFired, t.Events.Last().Type);
        Assert.Equal("toast", t.Events.Last().Note);
    }
}

using Ticktower.Core.Engine;
using Ticktower.Core.Models;

namespace Ticktower.Tests;

public class BoardLogicTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static TimerItem Started(TimerKind kind, int minutes, DateTime? at = null, string name = "t")
    {
        var t = new TimerItem { Name = name, Kind = kind, Duration = TimeSpan.FromMinutes(minutes) };
        TimerStateMachine.Start(t, at ?? T0);
        return t;
    }

    [Fact]
    public void Lanes_follow_state()
    {
        var running = Started(TimerKind.Countdown, 5);
        var expired = Started(TimerKind.Countdown, 5);
        TimerStateMachine.Expire(expired, T0.AddMinutes(6));
        var acked = Started(TimerKind.Countdown, 5);
        TimerStateMachine.Expire(acked, T0.AddMinutes(6));
        TimerStateMachine.Acknowledge(acked, T0.AddMinutes(7));
        var sw = Started(TimerKind.Stopwatch, 0);
        TimerStateMachine.Pause(sw, T0.AddMinutes(1));
        var stopped = Started(TimerKind.Stopwatch, 0);
        TimerStateMachine.Stop(stopped, T0.AddMinutes(1));

        Assert.Equal(Lane.Active, BoardLogic.LaneOf(running));
        Assert.Equal(Lane.Expired, BoardLogic.LaneOf(expired));
        Assert.Equal(Lane.Acknowledged, BoardLogic.LaneOf(acked));
        Assert.Equal(Lane.Active, BoardLogic.LaneOf(sw));
        Assert.Equal(Lane.Acknowledged, BoardLogic.LaneOf(stopped));
    }

    [Fact]
    public void Active_sorts_by_time_remaining_with_stopwatches_last()
    {
        var late = Started(TimerKind.Countdown, 30, name: "late");
        var soon = Started(TimerKind.Countdown, 5, name: "soon");
        var sw = Started(TimerKind.Stopwatch, 0, name: "sw");

        var order = BoardLogic.InLane(new[] { sw, late, soon }, Lane.Active).Select(t => t.Name);
        Assert.Equal(new[] { "soon", "late", "sw" }, order);
    }

    [Fact]
    public void Expired_sorts_most_overdue_first()
    {
        var a = Started(TimerKind.Countdown, 10, name: "a");
        var b = Started(TimerKind.Countdown, 5, name: "b");
        var now = T0.AddMinutes(20);
        TimerStateMachine.Expire(a, now);
        TimerStateMachine.Expire(b, now);

        Assert.Equal(new[] { "b", "a" }, BoardLogic.InLane(new[] { a, b }, Lane.Expired).Select(t => t.Name));
    }

    [Fact]
    public void Card_countdown_shows_remaining_then_overdue()
    {
        var t = Started(TimerKind.Countdown, 5);
        var running = BoardLogic.Card(t, T0.AddSeconds(90));
        Assert.Equal("03:30", running.TimeText);
        Assert.Equal(TimeSpan.FromSeconds(210), running.Remaining);

        TimerStateMachine.Expire(t, T0.AddMinutes(6));
        var expired = BoardLogic.Card(t, T0.AddMinutes(6));
        Assert.Equal("+01:00", expired.TimeText);
        Assert.Equal(TimeSpan.FromMinutes(1), expired.Overdue);
    }

    [Fact]
    public void Card_stopwatch_shows_elapsed_laps_and_bookmarks()
    {
        var t = Started(TimerKind.Stopwatch, 0);
        TimerStateMachine.Lap(t, T0.AddMinutes(1));
        TimerStateMachine.Bookmark(t, T0.AddMinutes(2), "x");
        TimerStateMachine.Bookmark(t, T0.AddMinutes(2));

        var c = BoardLogic.Card(t, T0.AddMinutes(61));
        Assert.Equal("1:01:00", c.TimeText);
        Assert.Equal(1, c.Laps);
        Assert.Equal(2, c.Bookmarks);
        Assert.Null(c.SinceClientUpdate);
    }

    [Fact]
    public void Card_interval_time_since_client_update_includes_acknowledge_delay()
    {
        var t = Started(TimerKind.Interval, 15);
        TimerStateMachine.Expire(t, T0.AddMinutes(15));
        TimerStateMachine.Acknowledge(t, T0.AddMinutes(20)); // acked 5 min late, restarts

        var c = BoardLogic.Card(t, T0.AddMinutes(25));
        Assert.Equal(TimeSpan.FromMinutes(5), c.SinceClientUpdate);
        Assert.Equal("05:00", c.SinceClientUpdateText);
        Assert.Equal("10:00", c.TimeText);

        var fresh = Started(TimerKind.Interval, 15);
        Assert.Equal(TimeSpan.FromMinutes(3), BoardLogic.Card(fresh, T0.AddMinutes(3)).SinceClientUpdate);
    }

    [Fact]
    public void FormatSpan_clamps_negative_and_adds_hours()
    {
        Assert.Equal("00:00", BoardLogic.FormatSpan(TimeSpan.FromSeconds(-5)));
        Assert.Equal("2:00:05", BoardLogic.FormatSpan(TimeSpan.FromSeconds(7205)));
    }
}

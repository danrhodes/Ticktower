using Ticktower.Core.Engine;
using Ticktower.Core.Models;
using Ticktower.Core.Storage;

namespace Ticktower.Tests;

public sealed class EventLogTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ticktower-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Stopwatch_logs_start_lap_bookmark_pause_resume_stop_in_order()
    {
        var t = new TimerItem { Kind = TimerKind.Stopwatch };
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Lap(t, T0.AddMinutes(1));
        TimerStateMachine.Bookmark(t, T0.AddMinutes(2), "note");
        TimerStateMachine.Pause(t, T0.AddMinutes(3));
        TimerStateMachine.Resume(t, T0.AddMinutes(4));
        TimerStateMachine.Stop(t, T0.AddMinutes(5));

        Assert.Equal(
            new[]
            {
                TimerEventType.Start, TimerEventType.Lap, TimerEventType.Bookmark,
                TimerEventType.Pause, TimerEventType.Resume, TimerEventType.Stop,
            },
            t.Events.Select(e => e.Type));
    }

    [Fact]
    public void Countdown_logs_start_expire_alert_snooze_expire_ack()
    {
        var t = new TimerItem { Kind = TimerKind.Countdown, Duration = TimeSpan.FromMinutes(5) };
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Expire(t, T0.AddMinutes(5));
        TimerStateMachine.RecordAlertFired(t, T0.AddMinutes(5));
        TimerStateMachine.Snooze(t, T0.AddMinutes(6), TimeSpan.FromMinutes(2));
        TimerStateMachine.Expire(t, T0.AddMinutes(8));
        TimerStateMachine.Acknowledge(t, T0.AddMinutes(9));

        Assert.Equal(
            new[]
            {
                TimerEventType.Start, TimerEventType.Expire, TimerEventType.AlertFired,
                TimerEventType.Snooze, TimerEventType.Expire, TimerEventType.Ack,
            },
            t.Events.Select(e => e.Type));
    }

    [Fact]
    public void Every_event_type_round_trips_through_json_with_utc_and_note()
    {
        var t = new TimerItem { Kind = TimerKind.Stopwatch };
        var types = Enum.GetValues<TimerEventType>();
        for (var i = 0; i < types.Length; i++)
            t.Events.Add(new TimerEvent(types[i], T0.AddSeconds(i), "n" + i));

        var path = Path.Combine(_dir, "state.json");
        var store = new JsonFileStore();
        store.Save(path, new StateFile { Timers = { t } });
        var back = store.Load(path, () => new StateFile()).Value.Timers.Single();

        Assert.Equal(types.Length, back.Events.Count);
        for (var i = 0; i < types.Length; i++)
        {
            Assert.Equal(types[i], back.Events[i].Type);
            Assert.Equal(T0.AddSeconds(i), back.Events[i].Utc);
            Assert.Equal(DateTimeKind.Utc, back.Events[i].Utc.Kind);
            Assert.Equal("n" + i, back.Events[i].Note);
        }
    }

    [Fact]
    public void Event_timestamps_never_decrease_across_a_full_lifecycle()
    {
        var t = new TimerItem { Kind = TimerKind.Interval, Duration = TimeSpan.FromMinutes(10) };
        TimerStateMachine.Start(t, T0);
        TimerStateMachine.Bookmark(t, T0.AddMinutes(3));
        TimerStateMachine.Expire(t, T0.AddMinutes(30));
        TimerStateMachine.Acknowledge(t, T0.AddMinutes(31));

        var times = t.Events.Select(e => e.Utc).ToArray();
        Assert.Equal(times.OrderBy(x => x), times);
    }
}

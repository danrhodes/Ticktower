using Ticktower.Core.Models;

namespace Ticktower.Core.Engine;

/// <summary>
/// Pure transitions over <see cref="TimerItem"/>. All times are absolute UTC.
/// Invalid transitions throw <see cref="InvalidOperationException"/>.
/// </summary>
public static class TimerStateMachine
{
    public static void Start(TimerItem t, DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        if (t.Events.Count > 0)
            throw new InvalidOperationException("Timer already started.");
        if (t.Kind != TimerKind.Stopwatch && t.Duration <= TimeSpan.Zero)
            throw new InvalidOperationException("Countdown and interval timers need a positive duration.");

        t.State = TimerState.Running;
        t.StartedUtc = nowUtc;
        t.EndUtc = t.Kind == TimerKind.Stopwatch ? null : nowUtc + t.Duration;
        Log(t, TimerEventType.Start, nowUtc);
    }

    /// <summary>
    /// Moves a running countdown or interval to Expired once <paramref name="nowUtc"/> has reached
    /// its end. The event is stamped with the true end time so late detection (sleep, relaunch)
    /// does not distort the log. Returns false when nothing changed.
    /// </summary>
    public static bool Expire(TimerItem t, DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        if (t.Kind == TimerKind.Stopwatch || t.State != TimerState.Running || t.EndUtc is null)
            return false;
        if (nowUtc < t.EndUtc.Value)
            return false;

        t.State = TimerState.Expired;
        Log(t, TimerEventType.Expire, t.EndUtc.Value);
        return true;
    }

    public static void Acknowledge(TimerItem t, DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        if (t.Kind == TimerKind.Stopwatch || t.State != TimerState.Expired)
            throw new InvalidOperationException("Only an expired countdown or interval can be acknowledged.");

        t.LastAckUtc = nowUtc;
        Log(t, TimerEventType.Ack, nowUtc);
        if (t.Kind == TimerKind.Interval)
        {
            t.State = TimerState.Running;
            t.EndUtc = nowUtc + t.Duration;
        }
        else
        {
            t.State = TimerState.Acknowledged;
        }
    }

    public static void Snooze(TimerItem t, DateTime nowUtc, TimeSpan length)
    {
        RequireUtc(nowUtc);
        if (length <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(length), "Snooze must be positive.");
        if (t.Kind == TimerKind.Stopwatch || t.State != TimerState.Expired)
            throw new InvalidOperationException("Only an expired timer can be snoozed.");

        t.State = TimerState.Running;
        t.EndUtc = nowUtc + length;
        Log(t, TimerEventType.Snooze, nowUtc, length.ToString("c"));
    }

    public static void Pause(TimerItem t, DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        RequireStopwatch(t, TimerState.Running, "paused");
        t.State = TimerState.Paused;
        Log(t, TimerEventType.Pause, nowUtc);
    }

    public static void Resume(TimerItem t, DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        RequireStopwatch(t, TimerState.Paused, "resumed");
        t.State = TimerState.Running;
        Log(t, TimerEventType.Resume, nowUtc);
    }

    public static void Stop(TimerItem t, DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        if (t.Kind != TimerKind.Stopwatch || t.State is not (TimerState.Running or TimerState.Paused))
            throw new InvalidOperationException("Only a running or paused stopwatch can be stopped.");
        t.State = TimerState.Stopped;
        Log(t, TimerEventType.Stop, nowUtc);
    }

    public static void Lap(TimerItem t, DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        RequireStopwatch(t, TimerState.Running, "lapped");
        Log(t, TimerEventType.Lap, nowUtc);
    }

    public static void Bookmark(TimerItem t, DateTime nowUtc, string? note = null)
    {
        RequireUtc(nowUtc);
        Log(t, TimerEventType.Bookmark, nowUtc, note);
    }

    public static void RecordAlertFired(TimerItem t, DateTime nowUtc, string? note = null)
    {
        RequireUtc(nowUtc);
        Log(t, TimerEventType.AlertFired, nowUtc, note);
    }

    /// <summary>Stopwatch running time, derived from the event log so it survives restarts.</summary>
    public static TimeSpan Elapsed(TimerItem t, DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        var total = TimeSpan.Zero;
        DateTime? segmentStart = null;
        foreach (var e in t.Events)
        {
            switch (e.Type)
            {
                case TimerEventType.Start:
                case TimerEventType.Resume:
                    segmentStart = e.Utc;
                    break;
                case TimerEventType.Pause:
                case TimerEventType.Stop:
                    if (segmentStart is not null)
                    {
                        total += e.Utc - segmentStart.Value;
                        segmentStart = null;
                    }
                    break;
            }
        }
        if (segmentStart is not null && t.State == TimerState.Running)
            total += nowUtc - segmentStart.Value;
        return total;
    }

    public static int LapCount(TimerItem t) => t.Events.Count(e => e.Type == TimerEventType.Lap);

    public static int BookmarkCount(TimerItem t) => t.Events.Count(e => e.Type == TimerEventType.Bookmark);

    private static void RequireStopwatch(TimerItem t, TimerState required, string verb)
    {
        if (t.Kind != TimerKind.Stopwatch || t.State != required)
            throw new InvalidOperationException($"Timer cannot be {verb} in its current state.");
    }

    private static void RequireUtc(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be DateTimeKind.Utc.", nameof(utc));
    }

    private static void Log(TimerItem t, TimerEventType type, DateTime utc, string? note = null) =>
        t.Events.Add(new TimerEvent(type, utc, note));
}

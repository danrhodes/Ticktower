using Ticktower.Core.Models;

namespace Ticktower.Core.Engine;

public enum Lane { Active, Expired, Acknowledged }

/// <summary>Display data for one card, all derived from stored UTC timestamps.</summary>
public sealed record CardInfo(
    Lane Lane,
    string TimeText,
    TimeSpan? Remaining,
    TimeSpan? Overdue,
    TimeSpan? Elapsed,
    int Laps,
    int Bookmarks,
    TimeSpan? SinceClientUpdate)
{
    public string SinceClientUpdateText => SinceClientUpdate is { } s ? BoardLogic.FormatSpan(s) : "";
}

public static class BoardLogic
{
    public static Lane LaneOf(TimerItem t) => t.State switch
    {
        TimerState.Running or TimerState.Paused => Lane.Active,
        TimerState.Expired => Lane.Expired,
        _ => Lane.Acknowledged,
    };

    /// <summary>
    /// Timers in a lane, sorted per the guide: Active by time remaining (stopwatches after, oldest first),
    /// Expired by time overdue (most overdue first), Acknowledged most recently finished first.
    /// </summary>
    public static IReadOnlyList<TimerItem> InLane(IEnumerable<TimerItem> timers, Lane lane)
    {
        var inLane = timers.Where(t => LaneOf(t) == lane);
        return lane switch
        {
            Lane.Active => inLane
                .OrderBy(t => t.EndUtc is null ? 1 : 0)
                .ThenBy(t => t.EndUtc ?? DateTime.MaxValue)
                .ThenBy(t => t.StartedUtc ?? DateTime.MaxValue)
                .ToList(),
            Lane.Expired => inLane.OrderBy(t => t.EndUtc ?? DateTime.MaxValue).ToList(),
            _ => inLane.OrderByDescending(t => t.Events.Count == 0 ? DateTime.MinValue : t.Events[^1].Utc).ToList(),
        };
    }

    public static CardInfo Card(TimerItem t, DateTime nowUtc)
    {
        var lane = LaneOf(t);
        TimeSpan? remaining = null, overdue = null, elapsed = null;
        string text;

        if (t.Kind == TimerKind.Stopwatch)
        {
            elapsed = TimerStateMachine.Elapsed(t, nowUtc);
            text = FormatSpan(elapsed.Value);
        }
        else if (t.State == TimerState.Expired && t.EndUtc is { } end)
        {
            overdue = nowUtc - end;
            text = "+" + FormatSpan(overdue.Value);
        }
        else if (t.State == TimerState.Running && t.EndUtc is { } due)
        {
            remaining = due - nowUtc;
            text = FormatSpan(remaining.Value);
        }
        else
        {
            text = "Done";
        }

        TimeSpan? since = null;
        if (t.Kind == TimerKind.Interval && (t.LastAckUtc ?? t.StartedUtc) is { } last)
            since = nowUtc - last;

        return new CardInfo(lane, text, remaining, overdue, elapsed,
            TimerStateMachine.LapCount(t), TimerStateMachine.BookmarkCount(t), since);
    }

    /// <summary>h:mm:ss, or mm:ss under an hour. Negative spans clamp to zero.</summary>
    public static string FormatSpan(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        var hours = (int)span.TotalHours;
        return hours > 0 ? $"{hours}:{span.Minutes:D2}:{span.Seconds:D2}" : $"{span.Minutes:D2}:{span.Seconds:D2}";
    }
}

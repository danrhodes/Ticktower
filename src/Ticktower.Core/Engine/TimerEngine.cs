using Ticktower.Core.Models;

namespace Ticktower.Core.Engine;

/// <summary>Receives expiry notifications. The UI layer implements the actual toast and sound.</summary>
public interface IAlertSink
{
    /// <param name="missed">True when the timer expired while the app was not running (sleep, kill, reboot).</param>
    void Expired(TimerItem timer, bool missed);
}

/// <summary>
/// Owns the live timers. Holds no tick counts: every decision is a comparison of the injected UTC
/// clock against stored absolute timestamps, so sleep and restarts cannot drift anything.
/// <see cref="Tick"/> exists to detect expiry; UI refresh of remaining time is derived from the same data.
/// </summary>
public sealed class TimerEngine
{
    private readonly List<TimerItem> _timers = new();
    private readonly IAlertSink _sink;
    private readonly Func<DateTime> _clock;

    public TimerEngine(IAlertSink sink, Func<DateTime>? clock = null)
    {
        _sink = sink;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>Raised after any successful change to timer data. Wire persistence here.</summary>
    public event Action? Changed;

    public IReadOnlyList<TimerItem> Timers => _timers;

    public StateFile Snapshot() => new() { Timers = _timers.ToList() };

    /// <summary>Replaces the timer set from stored state without ticking. Call <see cref="Recompute"/> next.</summary>
    public void Load(IEnumerable<TimerItem> timers)
    {
        _timers.Clear();
        _timers.AddRange(timers);
    }

    public TimerItem Add(TimerItem timer)
    {
        TimerStateMachine.Start(timer, Now());
        _timers.Add(timer);
        Changed?.Invoke();
        return timer;
    }

    /// <summary>Raised when a timer leaves the live set, with the close time. Wire the history writer here.</summary>
    public event Action<TimerItem, DateTime>? Closed;

    public bool Remove(Guid id)
    {
        var timer = _timers.FirstOrDefault(t => t.Id == id);
        if (timer is null) return false;
        _timers.Remove(timer);
        Closed?.Invoke(timer, Now());
        Changed?.Invoke();
        return true;
    }

    /// <summary>Launch-time pass: expires everything already due and fires those alerts immediately.</summary>
    public IReadOnlyList<TimerItem> Recompute() => Sweep(missed: true);

    /// <summary>Periodic pass. Returns timers that expired on this tick.</summary>
    public IReadOnlyList<TimerItem> Tick() => Sweep(missed: false);

    public void Acknowledge(Guid id) => Act(id, (t, now) => TimerStateMachine.Acknowledge(t, now));

    public void Snooze(Guid id, TimeSpan length) => Act(id, (t, now) => TimerStateMachine.Snooze(t, now, length));

    public void Pause(Guid id) => Act(id, (t, now) => TimerStateMachine.Pause(t, now));

    public void Resume(Guid id) => Act(id, (t, now) => TimerStateMachine.Resume(t, now));

    public void Stop(Guid id) => Act(id, (t, now) => TimerStateMachine.Stop(t, now));

    public void Lap(Guid id) => Act(id, (t, now) => TimerStateMachine.Lap(t, now));

    /// <summary>Applies descriptive edits (name, ticket, client, priority, notes) and raises <see cref="Changed"/>.</summary>
    public void Edit(Guid id, Action<TimerItem> edit) => Act(id, (t, _) => edit(t));

    public void Bookmark(Guid id, string? note = null) => Act(id, (t, now) => TimerStateMachine.Bookmark(t, now, note));

    private List<TimerItem> Sweep(bool missed)
    {
        var now = Now();
        var expired = new List<TimerItem>();
        foreach (var t in _timers)
        {
            if (!TimerStateMachine.Expire(t, now)) continue;
            TimerStateMachine.RecordAlertFired(t, now, missed ? "missed" : "expired");
            expired.Add(t);
        }

        if (expired.Count == 0) return expired;
        Changed?.Invoke();
        foreach (var t in expired) _sink.Expired(t, missed);
        return expired;
    }

    private void Act(Guid id, Action<TimerItem, DateTime> action)
    {
        var timer = _timers.FirstOrDefault(t => t.Id == id)
            ?? throw new KeyNotFoundException($"No timer {id}.");
        action(timer, Now());
        Changed?.Invoke();
    }

    private DateTime Now()
    {
        var now = _clock();
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Engine clock must return DateTimeKind.Utc.");
        return now;
    }
}

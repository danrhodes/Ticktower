using Ticktower.Core.Models;

namespace Ticktower.Core.Engine;

/// <summary>What the sink is asked to deliver: one rung of one timer's escalation ladder.</summary>
/// <param name="StepIndex">Zero-based rung. Repeats past the last rung reuse it.</param>
/// <param name="Missed">True when the timer expired while the app was not running (sleep, kill, reboot).</param>
public sealed record AlertNotice(TimerItem Timer, EffectiveAlert Alert, int StepIndex, bool Missed)
{
    public AlertStep Step => Alert.Steps[StepIndex];
}

/// <summary>Receives alerts. The UI layer implements the actual toast, sound, overlay and so on.</summary>
public interface IAlertSink
{
    void Alert(AlertNotice notice);
}

/// <summary>
/// Owns the live timers. Holds no tick counts: every decision is a comparison of the injected UTC
/// clock against stored absolute timestamps, so sleep and restarts cannot drift anything.
/// <see cref="Tick"/> exists to detect expiry and ladder repeats; UI refresh of remaining time is derived from the same data.
/// </summary>
public sealed class TimerEngine
{
    private static readonly TimeSpan MinRepeat = TimeSpan.FromSeconds(1);
    private static readonly IReadOnlyList<AlertProfile> DefaultProfiles = BuiltInAlertProfiles.Create();

    private readonly List<TimerItem> _timers = new();
    private readonly IAlertSink _sink;
    private readonly Func<DateTime> _clock;

    public TimerEngine(IAlertSink sink, Func<DateTime>? clock = null)
    {
        _sink = sink;
        _clock = clock ?? (() => DateTime.UtcNow);
        AlertSource = t => AlertResolver.Resolve(t, DefaultProfiles);
    }

    /// <summary>Raised after any successful change to timer data. Wire persistence here.</summary>
    public event Action? Changed;

    /// <summary>Raised when a timer leaves the live set, with the close time. Wire the history writer here.</summary>
    public event Action<TimerItem, DateTime>? Closed;

    /// <summary>Resolves a timer's escalation ladder. Defaults to the built-in Default profile.</summary>
    public Func<TimerItem, EffectiveAlert> AlertSource { get; set; }

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

    /// <summary>Adds a timer that waits (state Pending) until something starts it, such as a chain predecessor.</summary>
    public TimerItem AddPending(TimerItem timer)
    {
        timer.State = TimerState.Pending;
        _timers.Add(timer);
        Changed?.Invoke();
        return timer;
    }

    /// <summary>Adds <paramref name="first"/> running, and <paramref name="next"/> pending behind it via <c>NextTimerId</c>.</summary>
    public TimerItem AddChain(TimerItem first, TimerItem? next)
    {
        if (next is not null)
        {
            next.State = TimerState.Pending;
            _timers.Add(next);
            first.NextTimerId = next.Id;
        }
        return Add(first);
    }

    /// <summary>Starts a pending timer.</summary>
    public void Start(Guid id) => Act(id, (t, now) =>
    {
        if (t.State != TimerState.Pending)
            throw new InvalidOperationException("Only a pending timer can be started.");
        TimerStateMachine.Start(t, now);
    });

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

    public void Acknowledge(Guid id) => Act(id, (t, now) =>
    {
        TimerStateMachine.Acknowledge(t, now);
        StartChained(t, now);
    });

    public void Snooze(Guid id, TimeSpan length) => Act(id, (t, now) => TimerStateMachine.Snooze(t, now, length));

    public void Pause(Guid id) => Act(id, (t, now) => TimerStateMachine.Pause(t, now));

    public void Resume(Guid id) => Act(id, (t, now) => TimerStateMachine.Resume(t, now));

    public void Stop(Guid id) => Act(id, (t, now) =>
    {
        TimerStateMachine.Stop(t, now);
        StartChained(t, now);
    });

    public void Lap(Guid id) => Act(id, (t, now) => TimerStateMachine.Lap(t, now));

    /// <summary>Applies descriptive edits (name, ticket, client, priority, notes) and raises <see cref="Changed"/>.</summary>
    public void Edit(Guid id, Action<TimerItem> edit) => Act(id, (t, _) => edit(t));

    public void Bookmark(Guid id, string? note = null) => Act(id, (t, now) => TimerStateMachine.Bookmark(t, now, note));

    /// <summary>Chain rule: when A completes (acknowledged, or stopped for a stopwatch), a pending B starts.</summary>
    private void StartChained(TimerItem completed, DateTime now)
    {
        if (completed.NextTimerId is not { } nextId) return;
        var next = _timers.FirstOrDefault(t => t.Id == nextId);
        if (next is { State: TimerState.Pending })
            TimerStateMachine.Start(next, now);
    }

    private List<TimerItem> Sweep(bool missed)
    {
        var now = Now();
        var expired = new List<TimerItem>();
        var notices = new List<AlertNotice>();

        foreach (var t in _timers)
        {
            if (TimerStateMachine.Expire(t, now)) expired.Add(t);
            if (t.State != TimerState.Expired) continue;

            var alert = AlertSource(t);
            var step = NextDueStep(t, alert, now, out var alreadyFired);
            if (step < 0) continue;

            TimerStateMachine.RecordAlertFired(t, now, alreadyFired == 0 ? (missed ? "missed" : "expired") : $"repeat {alreadyFired}");
            notices.Add(new AlertNotice(t, alert, step, missed));
        }

        if (expired.Count == 0 && notices.Count == 0) return expired;
        Changed?.Invoke();
        foreach (var n in notices) _sink.Alert(n);
        return expired;
    }

    /// <summary>
    /// Which ladder rung is due now for an expired timer, or -1. Derived from the event log (alerts fired
    /// since the latest expiry), so it survives restarts and a snooze starts the ladder over.
    /// </summary>
    private static int NextDueStep(TimerItem t, EffectiveAlert alert, DateTime now, out int alreadyFired)
    {
        var lastExpire = t.Events.FindLastIndex(e => e.Type == TimerEventType.Expire);
        var fired = t.Events.Skip(lastExpire + 1).Where(e => e.Type == TimerEventType.AlertFired).ToList();
        alreadyFired = fired.Count;
        if (fired.Count == 0) return 0;

        var last = alert.Steps.Count - 1;
        var wait = alert.Steps[Math.Min(fired.Count - 1, last)].RepeatInterval;
        if (wait < MinRepeat) wait = MinRepeat;
        return now >= fired[^1].Utc + wait ? Math.Min(fired.Count, last) : -1;
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

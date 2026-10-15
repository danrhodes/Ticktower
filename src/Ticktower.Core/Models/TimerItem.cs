namespace Ticktower.Core.Models;

public sealed class TimerItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Ticket { get; set; } = "";
    public string Client { get; set; } = "";
    public Priority Priority { get; set; } = Priority.P3;
    public string? TypeId { get; set; }
    public TimerKind Kind { get; set; }
    public string Notes { get; set; } = "";
    public string? AlertProfileId { get; set; }
    public AlertOverride? AlertOverride { get; set; }
    public string? Hotkey { get; set; }
    public Guid? NextTimerId { get; set; }

    /// <summary>When set, completing this timer starts a fresh countdown with these details, which cycles back to this one (Pomodoro work/rest).</summary>
    public string? CycleName { get; set; }
    public string? CycleTypeId { get; set; }
    public TimeSpan? CycleDuration { get; set; }

    /// <summary>Length of a countdown or interval. Unused for stopwatches.</summary>
    public TimeSpan Duration { get; set; }

    public TimerState State { get; set; } = TimerState.Running;
    public DateTime? EndUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? LastAckUtc { get; set; }
    public List<TimerEvent> Events { get; set; } = new();
}

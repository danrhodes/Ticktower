namespace Ticktower.Core.Models;

public sealed class TimerEvent
{
    public TimerEventType Type { get; set; }
    public DateTime Utc { get; set; }
    public string? Note { get; set; }

    public TimerEvent() { }

    public TimerEvent(TimerEventType type, DateTime utc, string? note = null)
    {
        Type = type;
        Utc = utc;
        Note = note;
    }
}

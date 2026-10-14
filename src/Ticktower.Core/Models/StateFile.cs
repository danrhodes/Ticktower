namespace Ticktower.Core.Models;

/// <summary>Root of <c>state.json</c>.</summary>
public sealed class StateFile
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<TimerItem> Timers { get; set; } = new();
}

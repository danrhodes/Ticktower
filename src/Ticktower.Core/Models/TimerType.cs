namespace Ticktower.Core.Models;

public sealed class TimerType
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public TimerKind Kind { get; set; }
    public TimeSpan DefaultDuration { get; set; }
    public string? DefaultAlertProfileId { get; set; }
    public Priority DefaultPriority { get; set; } = Priority.P3;
    public string Colour { get; set; } = "#4A90D9";
    public string? Hotkey { get; set; }
    public bool BuiltIn { get; set; }

    /// <summary>Builds an unstarted timer from this type. The engine starts it on Add.</summary>
    public TimerItem CreateTimer(string name = "", string ticket = "", string client = "",
        Priority? priority = null, string notes = "") => new()
    {
        Name = string.IsNullOrWhiteSpace(name) ? Name : name,
        Ticket = ticket,
        Client = client,
        Priority = priority ?? DefaultPriority,
        Notes = notes,
        TypeId = Id,
        Kind = Kind,
        Duration = Kind == TimerKind.Stopwatch ? TimeSpan.Zero : DefaultDuration,
        AlertProfileId = DefaultAlertProfileId,
        Hotkey = Hotkey,
    };
}

/// <summary>Root of <c>types.json</c>.</summary>
public sealed class TypesFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<TimerType> Types { get; set; } = BuiltInTypes.Create();
}

public static class BuiltInTypes
{
    public static readonly int[] IntervalMinutes = { 5, 10, 15, 30, 60 };

    public static List<TimerType> Create()
    {
        var list = new List<TimerType>();
        foreach (var m in IntervalMinutes)
            list.Add(new TimerType
            {
                Id = $"interval-{m}", Name = $"{m} min", Kind = TimerKind.Interval,
                DefaultDuration = TimeSpan.FromMinutes(m), Colour = "#2E9E5B", BuiltIn = true,
            });
        list.Add(new TimerType
        {
            Id = "countdown", Name = "Countdown", Kind = TimerKind.Countdown,
            DefaultDuration = TimeSpan.FromMinutes(15), Colour = "#D98E2B", BuiltIn = true,
        });
        list.Add(new TimerType
        {
            Id = "stopwatch", Name = "Stopwatch", Kind = TimerKind.Stopwatch,
            Colour = "#7B61C9", BuiltIn = true,
        });
        return list;
    }
}

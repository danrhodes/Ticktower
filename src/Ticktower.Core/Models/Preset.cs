namespace Ticktower.Core.Models;

/// <summary>
/// A saved timer configuration, e.g. "Client X P1 = 15 min interval, escalation ladder 2".
/// Per user, importable and exportable as JSON.
/// </summary>
public sealed class Preset : ICatalogItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool BuiltIn { get; set; }

    public string TypeId { get; set; } = "";
    public string TimerName { get; set; } = "";
    public string Ticket { get; set; } = "";
    public string Client { get; set; } = "";
    public Priority Priority { get; set; } = Priority.P3;
    public string Notes { get; set; } = "";

    /// <summary>Overrides the type's default length. Ignored for stopwatches.</summary>
    public TimeSpan? Duration { get; set; }
    public string? AlertProfileId { get; set; }
    public AlertOverride? AlertOverride { get; set; }
    public string? Hotkey { get; set; }

    /// <summary>Type of a timer queued behind this one (chain).</summary>
    public string? ChainTypeId { get; set; }

    public override string ToString() => Name;

    public static Preset FromTimer(string name, TimerItem t, string? chainTypeId = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        TypeId = t.TypeId ?? "",
        TimerName = t.Name,
        Ticket = t.Ticket,
        Client = t.Client,
        Priority = t.Priority,
        Notes = t.Notes,
        Duration = t.Kind == TimerKind.Stopwatch ? null : t.Duration,
        AlertProfileId = t.AlertProfileId,
        AlertOverride = t.AlertOverride,
        Hotkey = t.Hotkey,
        ChainTypeId = chainTypeId,
    };

    /// <summary>Builds an unstarted timer, or null when the preset's type no longer exists.</summary>
    public TimerItem? CreateTimer(IReadOnlyCollection<TimerType> types)
    {
        var type = types.FirstOrDefault(t => t.Id == TypeId);
        if (type is null) return null;

        var timer = type.CreateTimer(TimerName, Ticket, Client, Priority, Notes);
        if (Duration is { } d && d > TimeSpan.Zero && type.Kind != TimerKind.Stopwatch) timer.Duration = d;
        if (AlertProfileId is not null) timer.AlertProfileId = AlertProfileId;
        timer.AlertOverride = AlertOverride;
        if (Hotkey is not null) timer.Hotkey = Hotkey;
        return timer;
    }
}

/// <summary>Root of <c>presets.json</c>.</summary>
public sealed class PresetsFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<Preset> Presets { get; set; } = new();
}

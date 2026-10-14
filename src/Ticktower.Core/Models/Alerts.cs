namespace Ticktower.Core.Models;

[Flags]
public enum AlertChannels
{
    None = 0,
    Sound = 1,
    Toast = 2,
    TaskbarFlash = 4,
    Overlay = 8,
    Tts = 16,
}

/// <summary>One rung of an escalation ladder. <see cref="RepeatInterval"/> is the wait before the next rung if unanswered.</summary>
public sealed class AlertStep
{
    public AlertChannels Channels { get; set; }
    public TimeSpan RepeatInterval { get; set; } = TimeSpan.FromSeconds(60);

    public AlertStep() { }

    public AlertStep(AlertChannels channels, TimeSpan repeat)
    {
        Channels = channels;
        RepeatInterval = repeat;
    }
}

public sealed class AlertProfile : ICatalogItem
{
    public const string DefaultTtsTemplate = "{name} {ticket} {client} has expired";

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<AlertStep> Steps { get; set; } = new();
    public string? SoundFile { get; set; }
    /// <summary>0 to 100.</summary>
    public int Volume { get; set; } = 80;
    public string TtsTemplate { get; set; } = DefaultTtsTemplate;
    public bool BuiltIn { get; set; }

    public override string ToString() => Name;
}

/// <summary>Per-timer override of any profile field. Null means inherit from the profile.</summary>
public sealed class AlertOverride
{
    public List<AlertStep>? Steps { get; set; }
    public string? SoundFile { get; set; }
    public int? Volume { get; set; }
    public string? TtsTemplate { get; set; }

    public bool IsEmpty => Steps is not { Count: > 0 } && SoundFile is null && Volume is null && TtsTemplate is null;
}

/// <summary>Root of <c>alertprofiles.json</c>.</summary>
public sealed class AlertProfilesFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<AlertProfile> Profiles { get; set; } = BuiltInAlertProfiles.Create();
}

/// <summary>A profile with any per-timer override applied.</summary>
public sealed record EffectiveAlert(IReadOnlyList<AlertStep> Steps, string? SoundFile, int Volume, string TtsTemplate);

public static class BuiltInAlertProfiles
{
    public const string DefaultId = "default";
    public const string UrgentId = "urgent";

    public static List<AlertProfile> Create() => new()
    {
        new AlertProfile
        {
            Id = DefaultId, Name = "Default", BuiltIn = true,
            Steps =
            {
                new(AlertChannels.Toast, TimeSpan.FromSeconds(60)),
                new(AlertChannels.Toast | AlertChannels.Sound, TimeSpan.FromSeconds(60)),
                new(AlertChannels.Overlay | AlertChannels.Sound, TimeSpan.FromSeconds(60)),
                new(AlertChannels.Overlay | AlertChannels.Tts, TimeSpan.FromSeconds(60)),
            },
        },
        new AlertProfile
        {
            Id = UrgentId, Name = "Urgent", BuiltIn = true, Volume = 100,
            Steps =
            {
                new(AlertChannels.Toast | AlertChannels.Sound | AlertChannels.TaskbarFlash, TimeSpan.FromSeconds(30)),
                new(AlertChannels.Overlay | AlertChannels.Sound | AlertChannels.Tts, TimeSpan.FromSeconds(30)),
            },
        },
    };
}

public static class AlertResolver
{
    private static readonly AlertProfile Fallback = BuiltInAlertProfiles.Create()[0];

    /// <summary>
    /// The timer's profile (or Default when unset or missing) with its override layered on top.
    /// The result always has at least one step.
    /// </summary>
    public static EffectiveAlert Resolve(TimerItem timer, IEnumerable<AlertProfile> profiles)
    {
        var list = profiles as IList<AlertProfile> ?? profiles.ToList();
        var profile = list.FirstOrDefault(p => p.Id == timer.AlertProfileId)
            ?? list.FirstOrDefault(p => p.Id == BuiltInAlertProfiles.DefaultId)
            ?? Fallback;
        var o = timer.AlertOverride;

        var steps = o?.Steps is { Count: > 0 } ? o.Steps : profile.Steps;
        if (steps.Count == 0) steps = Fallback.Steps;

        return new EffectiveAlert(
            steps,
            o?.SoundFile ?? profile.SoundFile,
            Math.Clamp(o?.Volume ?? profile.Volume, 0, 100),
            o?.TtsTemplate ?? profile.TtsTemplate);
    }
}

public static class AlertText
{
    /// <summary>Fills {name}, {ticket}, {client}, {priority}, {type}. Extra spaces from empty fields are collapsed.</summary>
    public static string Format(string template, TimerItem timer)
    {
        var text = template
            .Replace("{name}", timer.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{ticket}", timer.Ticket, StringComparison.OrdinalIgnoreCase)
            .Replace("{client}", timer.Client, StringComparison.OrdinalIgnoreCase)
            .Replace("{priority}", timer.Priority.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{type}", timer.TypeId ?? "", StringComparison.OrdinalIgnoreCase);
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

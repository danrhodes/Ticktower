namespace Ticktower.Core.Models;

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>Where one window sat on each monitor, and which monitor it was last on.</summary>
public sealed class WindowMemory
{
    public string? LastMonitor { get; set; }
    public Dictionary<string, WindowPlacement> ByMonitor { get; set; } = new();
}

/// <summary>A monitor's working area, in the same units as <see cref="WindowPlacement"/>.</summary>
public readonly record struct MonitorInfo(string Name, bool IsPrimary, double Left, double Top, double Width, double Height);

public static class WindowMemoryLogic
{
    public static void Remember(SettingsFile settings, string windowId, string monitorName, WindowPlacement placement)
    {
        if (!settings.Windows.TryGetValue(windowId, out var memory))
            settings.Windows[windowId] = memory = new WindowMemory();
        memory.LastMonitor = monitorName;
        memory.ByMonitor[monitorName] = placement;
    }

    /// <summary>
    /// The placement to open a window at, kept fully inside its monitor. Uses the monitor it was last on; if that
    /// is unplugged, falls back to the primary monitor's saved spot, then to any monitor with one. Null means no
    /// usable memory, so use the window's default.
    /// </summary>
    public static (WindowPlacement Placement, MonitorInfo Monitor)? Restore(SettingsFile settings, string windowId, IReadOnlyList<MonitorInfo> monitors)
    {
        if (!settings.Windows.TryGetValue(windowId, out var memory) || monitors.Count == 0) return null;

        MonitorInfo? WithMemory(MonitorInfo m) => memory.ByMonitor.ContainsKey(m.Name) ? m : null;

        MonitorInfo? monitor = memory.LastMonitor is { } last && monitors.FirstOrDefault(m => m.Name == last) is { Name: not null } lastMon
            ? WithMemory(lastMon)
            : null;
        monitor ??= monitors.Where(m => m.IsPrimary).Select(WithMemory).FirstOrDefault(m => m is not null);
        monitor ??= monitors.Select(WithMemory).FirstOrDefault(m => m is not null);
        if (monitor is not { } mon) return null;

        var saved = memory.ByMonitor[mon.Name];
        var width = Math.Min(Math.Max(saved.Width, 1), mon.Width);
        var height = Math.Min(Math.Max(saved.Height, 1), mon.Height);
        var placement = new WindowPlacement
        {
            Width = width,
            Height = height,
            Left = Math.Clamp(saved.Left, mon.Left, mon.Left + mon.Width - width),
            Top = Math.Clamp(saved.Top, mon.Top, mon.Top + mon.Height - height),
            Maximized = saved.Maximized,
        };
        return (placement, mon);
    }
}

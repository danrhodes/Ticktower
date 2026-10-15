using System.Globalization;

namespace Ticktower.Core.Models;

public enum ColourMode { Urgency, Priority, Type, Client }

/// <summary>App appearance. System follows the Windows "app mode" setting.</summary>
public enum ThemeMode { System, Light, Dark }

/// <summary>Root of <c>settings.json</c>.</summary>
public sealed class SettingsFile
{
    public const int CurrentVersion = 4;

    public int Version { get; set; } = CurrentVersion;
    public List<int> SnoozeMinutes { get; set; } = new() { 5, 10, 15, 30 };
    public ColourMode ColourMode { get; set; } = ColourMode.Urgency;
    public string? PaletteId { get; set; }
    public bool BoardAlwaysOnTop { get; set; }
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>Ask GitHub Releases for a newer version on start and then daily. The only network use in the app.</summary>
    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>A release version the user chose to skip; it is not announced again.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>Remembered position and size per window id, per monitor.</summary>
    public Dictionary<string, WindowMemory> Windows { get; set; } = new();

    /// <summary>The first preset, used by one-click snooze (toast and overlay buttons).</summary>
    public TimeSpan QuickSnooze => TimeSpan.FromMinutes(SnoozeMinutes.FirstOrDefault(m => m > 0, 5));
}

public static class SnoozeParser
{
    public static readonly TimeSpan Max = TimeSpan.FromHours(24);

    /// <summary>
    /// Parses "5" (minutes), "5m", "90s", "2h", or combinations such as "1h30m". Range 1 second to 24 hours.
    /// </summary>
    public static bool TryParse(string? text, out TimeSpan result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim().ToLowerInvariant().Replace(" ", "");
        if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var bareMinutes))
            return InRange(TimeSpan.FromMinutes(bareMinutes), out result);

        var total = TimeSpan.Zero;
        var digits = "";
        foreach (var c in s)
        {
            if (char.IsAsciiDigit(c)) { digits += c; continue; }
            if (digits.Length == 0 || digits.Length > 6) return false;
            var n = int.Parse(digits, CultureInfo.InvariantCulture);
            digits = "";
            total += c switch
            {
                'h' => TimeSpan.FromHours(n),
                'm' => TimeSpan.FromMinutes(n),
                's' => TimeSpan.FromSeconds(n),
                _ => TimeSpan.MinValue,
            };
            if (total < TimeSpan.Zero) return false;
        }
        return digits.Length == 0 && InRange(total, out result);
    }

    public static string Format(TimeSpan span)
    {
        if (span.TotalHours >= 1 && span.Minutes == 0 && span.Seconds == 0) return $"{(int)span.TotalHours}h";
        if (span.TotalMinutes >= 1 && span.Seconds == 0) return $"{(int)span.TotalMinutes}m";
        if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds}s";
        return BoardFormat(span);
    }

    private static string BoardFormat(TimeSpan span) =>
        $"{(int)span.TotalHours}h{span.Minutes:D2}m{span.Seconds:D2}s";

    private static bool InRange(TimeSpan value, out TimeSpan result)
    {
        result = value;
        return value >= TimeSpan.FromSeconds(1) && value <= Max;
    }
}

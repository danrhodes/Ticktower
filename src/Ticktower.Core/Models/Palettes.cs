using System.Globalization;
using Ticktower.Core.Engine;

namespace Ticktower.Core.Models;

public sealed class ColourPalette : ICatalogItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool BuiltIn { get; set; }

    public string UrgencyGreen { get; set; } = "#2E9E5B";
    public string UrgencyAmber { get; set; } = "#E0A526";
    public string UrgencyRed { get; set; } = "#D32F2F";

    /// <summary>P1 to P4, in order.</summary>
    public List<string> Priority { get; set; } = new() { "#C62828", "#E67E22", "#2E7D9E", "#6B7A86" };

    /// <summary>Clients take a stable colour from this list, chosen by a hash of the client name.</summary>
    public List<string> Client { get; set; } = new()
    {
        "#3B82C4", "#8E5CC9", "#C2578A", "#2E9E8F", "#B8860B", "#5C7CFA", "#7CB342", "#D2691E",
    };

    /// <summary>Stopwatches, queued timers and blank clients.</summary>
    public string Neutral { get; set; } = "#7B8794";

    /// <summary>Acknowledged and stopped timers.</summary>
    public string Done { get; set; } = "#B0B7BE";

    public override string ToString() => Name;

    public ColourPalette Clone() => new()
    {
        Id = Id, Name = Name, BuiltIn = BuiltIn, UrgencyGreen = UrgencyGreen, UrgencyAmber = UrgencyAmber,
        UrgencyRed = UrgencyRed, Priority = Priority.ToList(), Client = Client.ToList(), Neutral = Neutral, Done = Done,
    };
}

/// <summary>Root of <c>palettes.json</c>.</summary>
public sealed class PalettesFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<ColourPalette> Palettes { get; set; } = BuiltInPalettes.Create();
}

public static class BuiltInPalettes
{
    public const string DefaultId = "default";

    public static List<ColourPalette> Create() => new()
    {
        new ColourPalette { Id = DefaultId, Name = "Default", BuiltIn = true },
        new ColourPalette
        {
            Id = "high-contrast", Name = "High contrast", BuiltIn = true,
            UrgencyGreen = "#00A000", UrgencyAmber = "#FFB000", UrgencyRed = "#E00000",
            Priority = new() { "#E00000", "#FF8000", "#0050C0", "#404040" },
            Client = new() { "#0050C0", "#7A00C0", "#C00070", "#007A6E", "#806000", "#2030C0", "#408000", "#A04000" },
            Neutral = "#404040", Done = "#909090",
        },
    };
}

public static class ColourUtil
{
    public static bool TryParseHex(string? hex, out (byte R, byte G, byte B) rgb)
    {
        rgb = default;
        if (hex is not { Length: 7 } || hex[0] != '#') return false;
        if (!byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return false;
        if (!byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return false;
        if (!byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
        rgb = (r, g, b);
        return true;
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>Linear blend. <paramref name="t"/> 0 gives <paramref name="from"/>, 1 gives <paramref name="to"/>. Bad hex yields <paramref name="from"/>.</summary>
    public static string Lerp(string from, string to, double t)
    {
        if (!TryParseHex(from, out var a) || !TryParseHex(to, out var b)) return from;
        t = Math.Clamp(t, 0, 1);
        byte Mix(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return ToHex(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }
}

public static class BoardColours
{
    /// <summary>Card accent colour (hex) for the chosen mode.</summary>
    public static string For(TimerItem t, ColourMode mode, DateTime nowUtc, ColourPalette palette, IEnumerable<TimerType> types) =>
        mode switch
        {
            ColourMode.Priority => At(palette.Priority, (int)t.Priority, palette.Neutral),
            ColourMode.Type => types.FirstOrDefault(x => x.Id == t.TypeId)?.Colour ?? palette.Neutral,
            ColourMode.Client => string.IsNullOrWhiteSpace(t.Client) || palette.Client.Count == 0
                ? palette.Neutral
                : palette.Client[(int)(StableHash(t.Client.Trim().ToLowerInvariant()) % (uint)palette.Client.Count)],
            _ => Urgency(t, nowUtc, palette),
        };

    /// <summary>Green while plenty of time is left, blending to amber at 50% and red at 0%. Expired is red.</summary>
    public static string Urgency(TimerItem t, DateTime nowUtc, ColourPalette p)
    {
        switch (BoardLogic.LaneOf(t))
        {
            case Lane.Acknowledged: return p.Done;
            case Lane.Expired: return p.UrgencyRed;
        }
        if (t.State != TimerState.Running || t.Kind == TimerKind.Stopwatch || t.EndUtc is not { } end || t.Duration <= TimeSpan.Zero)
            return p.Neutral;

        var left = Math.Clamp((end - nowUtc) / t.Duration, 0, 1);
        return left >= 0.5 ? p.UrgencyGreen
            : left >= 0.25 ? ColourUtil.Lerp(p.UrgencyGreen, p.UrgencyAmber, (0.5 - left) / 0.25)
            : ColourUtil.Lerp(p.UrgencyAmber, p.UrgencyRed, (0.25 - left) / 0.25);
    }

    /// <summary>FNV-1a, so a client keeps its colour across runs (string.GetHashCode is randomised per process).</summary>
    public static uint StableHash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s)
        {
            h ^= c;
            h *= 16777619u;
        }
        return h;
    }

    private static string At(List<string> list, int i, string fallback) => i >= 0 && i < list.Count ? list[i] : fallback;
}

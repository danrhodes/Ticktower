namespace Ticktower.Core;

/// <summary>Values match the Win32 <c>MOD_*</c> flags used by <c>RegisterHotKey</c>.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A global hotkey such as <c>Ctrl+Alt+F5</c>. Needs at least one modifier. Keys: A-Z, 0-9, F1-F24.</summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, string Key)
{
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            var part = raw.ToUpperInvariant();
            switch (part)
            {
                case "CTRL" or "CONTROL": mods |= HotkeyModifiers.Ctrl; break;
                case "ALT": mods |= HotkeyModifiers.Alt; break;
                case "SHIFT": mods |= HotkeyModifiers.Shift; break;
                case "WIN" or "WINDOWS": mods |= HotkeyModifiers.Win; break;
                default:
                    if (key is not null || !IsValidKey(part)) return false;
                    key = part;
                    break;
            }
        }

        if (key is null || mods == HotkeyModifiers.None) return false;
        hotkey = new Hotkey(mods, key);
        return true;
    }

    /// <summary>Win32 virtual-key code.</summary>
    public int VirtualKey => Key.Length == 1 ? Key[0] : 0x70 + int.Parse(Key.AsSpan(1)) - 1;

    /// <summary>Canonical text, e.g. <c>Ctrl+Alt+F5</c>. Equal hotkeys always render identically.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    private static bool IsValidKey(string k)
    {
        if (k.Length == 0) return false;
        if (k.Length == 1) return char.IsAsciiLetterUpper(k[0]) || char.IsAsciiDigit(k[0]);
        return k[0] == 'F' && int.TryParse(k.AsSpan(1), out var n) && n is >= 1 and <= 24 && k.Length <= 3;
    }
}

/// <summary>Tracks which owner holds which hotkey so duplicates are rejected at assignment.</summary>
public sealed class HotkeyBook
{
    private readonly Dictionary<Hotkey, string> _owners = new();

    /// <summary>Assigns the hotkey to the owner, replacing any previous hotkey that owner held.</summary>
    /// <param name="conflictOwner">The existing owner when the hotkey is taken by someone else.</param>
    public bool TryAssign(string ownerId, Hotkey hotkey, out string? conflictOwner)
    {
        if (_owners.TryGetValue(hotkey, out var existing) && existing != ownerId)
        {
            conflictOwner = existing;
            return false;
        }
        Release(ownerId);
        _owners[hotkey] = ownerId;
        conflictOwner = null;
        return true;
    }

    public void Release(string ownerId)
    {
        foreach (var key in _owners.Where(kv => kv.Value == ownerId).Select(kv => kv.Key).ToList())
            _owners.Remove(key);
    }

    public string? OwnerOf(Hotkey hotkey) => _owners.GetValueOrDefault(hotkey);
}

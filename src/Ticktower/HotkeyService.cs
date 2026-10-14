using System.ComponentModel;
using System.Windows.Interop;
using Ticktower.Core;

namespace Ticktower;

/// <summary>
/// Registers global hotkeys with Win32 <c>RegisterHotKey</c> on a message-only window.
/// Duplicates inside Ticktower are rejected by <see cref="HotkeyBook"/> before Windows is asked;
/// clashes with other apps surface as the Win32 failure message.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly HotkeyBook _book = new();
    private readonly Dictionary<int, string> _ownerById = new();
    private readonly Dictionary<string, (int Id, string Text)> _registered = new();
    private int _nextId = 1;

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("TicktowerHotkeys")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            Width = 0,
            Height = 0,
        });
        _source.AddHook(WndProc);
    }

    /// <summary>Raised with the owner id (<c>type:{id}</c> or <c>timer:{guid}</c>) when its hotkey is pressed.</summary>
    public event Action<string>? Pressed;

    /// <summary>Registrations that failed, for display. Keyed by owner id.</summary>
    public Dictionary<string, string> Failures { get; } = new();

    public string? TextFor(string ownerId) => _registered.TryGetValue(ownerId, out var r) ? r.Text : null;

    /// <summary>
    /// Assigns <paramref name="text"/> to the owner, replacing its previous hotkey. A blank text just clears it.
    /// Returns an error message, or null on success.
    /// </summary>
    public string? Register(string ownerId, string? text)
    {
        Failures.Remove(ownerId);
        if (string.IsNullOrWhiteSpace(text))
        {
            Unregister(ownerId);
            return null;
        }
        if (!Hotkey.TryParse(text, out var hotkey))
            return $"\"{text}\" is not a valid hotkey. Use at least one of Ctrl, Alt, Shift or Win plus a letter, digit or F-key.";
        if (_registered.TryGetValue(ownerId, out var current) && current.Text == hotkey.ToString())
            return null;

        var previous = _registered.TryGetValue(ownerId, out var old) ? old : ((int, string)?)null;
        if (!_book.TryAssign(ownerId, hotkey, out var conflict))
            return $"{hotkey} is already assigned to {Describe(conflict!)}.";

        var id = _nextId++;
        var mods = (uint)hotkey.Modifiers | NativeMethods.MOD_NOREPEAT;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, mods, (uint)hotkey.VirtualKey))
        {
            var reason = new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()).Message;
            // Put the owner's previous hotkey back in the book; Windows still holds it.
            if (previous is { } p && Hotkey.TryParse(p.Item2, out var prevKey)) _book.TryAssign(ownerId, prevKey, out _);
            else _book.Release(ownerId);
            return $"Windows could not register {hotkey} (another app probably owns it): {reason}";
        }

        if (previous is { } prev)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, prev.Item1);
            _ownerById.Remove(prev.Item1);
        }
        _ownerById[id] = ownerId;
        _registered[ownerId] = (id, hotkey.ToString());
        return null;
    }

    public void Unregister(string ownerId)
    {
        if (_registered.Remove(ownerId, out var r))
        {
            NativeMethods.UnregisterHotKey(_source.Handle, r.Id);
            _ownerById.Remove(r.Id);
        }
        _book.Release(ownerId);
        Failures.Remove(ownerId);
    }

    public void Dispose()
    {
        foreach (var id in _ownerById.Keys.ToList()) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _ownerById.Clear();
        _registered.Clear();
        _source.Dispose();
    }

    private static string Describe(string ownerId) =>
        ownerId.StartsWith("type:", StringComparison.Ordinal) ? "another timer type" : "another timer";

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _ownerById.TryGetValue(wParam.ToInt32(), out var owner))
        {
            handled = true;
            Pressed?.Invoke(owner);
        }
        return IntPtr.Zero;
    }
}

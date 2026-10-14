using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>
/// Remembers a window's position and size per monitor and puts it back on the monitor it was last on.
/// The saved spot is kept on-screen by <see cref="WindowMemoryLogic"/>, so an unplugged monitor or a
/// lower resolution never strands a window off-screen.
/// </summary>
public static class WindowPlacer
{
    /// <param name="restoreSize">False for windows that size themselves to their content.</param>
    public static void Attach(Window window, string id, AppHost host, bool restoreSize)
    {
        var save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        save.Tick += (_, _) =>
        {
            save.Stop();
            Save(window, id, host);
        };
        void Dirty()
        {
            if (window.IsLoaded && window.WindowState != WindowState.Minimized) { save.Stop(); save.Start(); }
        }

        window.SourceInitialized += (_, _) => Restore(window, id, host, restoreSize);
        window.LocationChanged += (_, _) => Dirty();
        window.SizeChanged += (_, _) => Dirty();
        window.StateChanged += (_, _) => Dirty();
        // Hiding to the tray or exiting must not lose a move made in the last second.
        window.IsVisibleChanged += (_, _) =>
        {
            if (!window.IsVisible && save.IsEnabled)
            {
                save.Stop();
                Save(window, id, host);
            }
        };
    }

    private static List<MonitorInfo> Monitors(Window window)
    {
        // Screen bounds are device pixels; WPF positions are device-independent units.
        var scale = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        return System.Windows.Forms.Screen.AllScreens
            .Select(s => new MonitorInfo(s.DeviceName, s.Primary,
                s.WorkingArea.Left * scale, s.WorkingArea.Top * scale, s.WorkingArea.Width * scale, s.WorkingArea.Height * scale))
            .ToList();
    }

    private static void Restore(Window window, string id, AppHost host, bool restoreSize)
    {
        if (WindowMemoryLogic.Restore(host.Settings, id, Monitors(window)) is not { } found) return;
        var p = found.Placement;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = p.Left;
        window.Top = p.Top;
        if (restoreSize && window.ResizeMode != ResizeMode.NoResize)
        {
            window.Width = p.Width;
            window.Height = p.Height;
        }
        if (p.Maximized && restoreSize) window.WindowState = WindowState.Maximized;
    }

    private static void Save(Window window, string id, AppHost host)
    {
        if (window.WindowState == WindowState.Minimized) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var maximized = window.WindowState == WindowState.Maximized;
        var bounds = maximized ? window.RestoreBounds : new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1) return;

        var monitor = System.Windows.Forms.Screen.FromHandle(handle).DeviceName;
        WindowMemoryLogic.Remember(host.Settings, id, monitor, new WindowPlacement
        {
            Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height, Maximized = maximized,
        });
        host.PersistSettings();
    }
}

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>
/// Swaps the app between the stock Windows look (Light) and a dark skin. Dark merges custom control templates and brushes
/// into the application resources; Light leaves only the handful of brushes the app's own XAML refers to.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _active;
    private static ThemeMode _mode = ThemeMode.System;
    private static bool _hooked;

    public static bool IsDark { get; private set; }

    public static void Apply(ThemeMode mode)
    {
        _mode = mode;
        Hook();

        var dark = mode == ThemeMode.Dark || (mode == ThemeMode.System && !WindowsUsesLightApps());
        IsDark = dark;

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_active is not null) merged.Remove(_active);
        _active = new ResourceDictionary { Source = new Uri($"/Ticktower;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        merged.Add(_active);

        foreach (Window w in Application.Current.Windows) ApplyToWindow(w);
    }

    private static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General || _mode != ThemeMode.System) return;
            Application.Current.Dispatcher.BeginInvoke(() => Apply(ThemeMode.System));
        };
    }


    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window w) ApplyToWindow(w);
    }

    private static void ApplyToWindow(Window w)
    {
        ApplyBrushes(w);
        ApplyTitleBar(w);
    }

    /// <summary>Windows that set no colours of their own follow the theme. Ones that do (board, overlay) keep theirs.</summary>
    private static void ApplyBrushes(Window w)
    {
        if (w.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue)
            w.SetResourceReference(Control.BackgroundProperty, "WindowBg");
        if (w.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue)
            w.SetResourceReference(Control.ForegroundProperty, "Text");
    }

    private static void ApplyTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        var on = IsDark ? 1 : 0;
        // 20 is DWMWA_USE_IMMERSIVE_DARK_MODE (19 on early Windows 10 builds). Failure just leaves the light title bar.
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }

    private static bool WindowsUsesLightApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>Small floating window with one button per timer type. A click opens the entry window; right-click edits the type.</summary>
public partial class LauncherWindow : Window
{
    private readonly AppHost _host;

    public LauncherWindow(AppHost host)
    {
        InitializeComponent();
        _host = host;
        host.CatalogChanged += Refresh;
        host.Ticked += UpdateStatus;
        Refresh();
    }

    private void Refresh()
    {
        TypeButtons.ItemsSource = null;
        TypeButtons.ItemsSource = _host.Types;
        PresetButtons.ItemsSource = null;
        PresetButtons.ItemsSource = _host.Presets;
        var any = _host.Presets.Count > 0;
        PresetLabel.Visibility = PresetButtons.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus();
    }

    /// <summary>Hotkey registration failures stay visible here until fixed.</summary>
    private void UpdateStatus()
    {
        var failures = _host.HotkeyFailures;
        Status.Text = string.Join(Environment.NewLine, failures);
        Status.Visibility = failures.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnTypeClick(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is TimerType type)
            new EntryWindow(_host, type).Show();
    }

    private void OnTypeRightClick(object sender, MouseButtonEventArgs e)
    {
        if (((Button)sender).Tag is TimerType type)
            new TypeEditWindow(_host, type).ShowDialog();
        e.Handled = true;
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is Preset preset)
            new EntryWindow(_host, preset: preset).Show();
    }

    private void OnNewType(object sender, RoutedEventArgs e) => new TypeEditWindow(_host).ShowDialog();

    private void OnExport(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowExport();

    private void OnSettings(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowSettings();
}

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
        UpdateButtons.ItemsSource = null;
        UpdateButtons.ItemsSource = _host.Types.Where(t => t.Kind == TimerKind.Interval).ToList();
        TypeButtons.ItemsSource = null;
        TypeButtons.ItemsSource = _host.Types.Where(t => t.Kind != TimerKind.Interval).ToList();
        PresetButtons.ItemsSource = null;
        PresetButtons.ItemsSource = _host.Presets;
        var any = _host.Presets.Count > 0;
        PresetLabel.Visibility = PresetButtons.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        var s = _host.Settings;
        PomodoroButton.Content = $"Pomodoro {SnoozeParser.Format(s.PomodoroWork)}/{SnoozeParser.Format(s.PomodoroRest)}";
        PomodoroButton.ToolTip = "Work then rest, repeating. Right-click to change the lengths.";
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

    private void OnPomodoro(object sender, RoutedEventArgs e) => _host.StartPomodoro();

    private void OnPomodoroRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var work = new TextBox { Text = SnoozeParser.Format(_host.Settings.PomodoroWork), Margin = new Thickness(0, 2, 0, 8), MinWidth = 160 };
        var rest = new TextBox { Text = SnoozeParser.Format(_host.Settings.PomodoroRest), Margin = new Thickness(0, 2, 0, 10) };
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Work length (e.g. 25m)" });
        panel.Children.Add(work);
        panel.Children.Add(new TextBlock { Text = "Rest length (e.g. 5m)" });
        panel.Children.Add(rest);
        var window = new Window
        {
            Title = "Pomodoro lengths", Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true,
        };
        panel.Children.Add(FormBuilder.OkCancel(window, () =>
        {
            if (!SnoozeParser.TryParse(work.Text, out var w) || !SnoozeParser.TryParse(rest.Text, out var r))
            {
                FormBuilder.Error("Enter lengths like 25m, 90s or 1h30m.");
                return false;
            }
            _host.Settings.PomodoroWork = w;
            _host.Settings.PomodoroRest = r;
            _host.SaveSettings();
            return true;
        }));
        window.ShowDialog();
    }

    private void OnNewType(object sender, RoutedEventArgs e) => new TypeEditWindow(_host).ShowDialog();

    private void OnExport(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowExport();

    private void OnSettings(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowSettings();
}

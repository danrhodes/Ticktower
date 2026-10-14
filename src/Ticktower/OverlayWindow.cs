using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ticktower.Core.Engine;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>Full-screen, topmost alert for an expired timer. Stays until acknowledged or snoozed.</summary>
public sealed class OverlayWindow : Window
{
    private readonly AppHost _host;
    private readonly TimerItem _timer;
    private readonly TextBlock _overdue;
    private bool _closing;

    public OverlayWindow(AppHost host, TimerItem timer)
    {
        _host = host;
        _timer = timer;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x8E, 0x1B, 0x1B));

        var detail = string.Join(" · ", new[] { timer.Ticket, timer.Client }.Where(s => !string.IsNullOrWhiteSpace(s)));
        _overdue = Big("", 56);

        var snooze = new Button { Content = $"Snooze {SnoozeParser.Format(host.Settings.QuickSnooze)}", Padding = new Thickness(28, 12, 28, 12), FontSize = 20, Margin = new Thickness(8) };
        var ack = new Button { Content = "Acknowledge", Padding = new Thickness(28, 12, 28, 12), FontSize = 20, Margin = new Thickness(8), IsDefault = true };
        ack.Click += (_, _) => AppHost.Safe(() => host.Engine.Acknowledge(timer.Id));
        snooze.Click += (_, _) => AppHost.Safe(() => host.Engine.Snooze(timer.Id, host.Settings.QuickSnooze));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
        buttons.Children.Add(ack);
        buttons.Children.Add(snooze);

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(Big("TIMER EXPIRED", 28));
        panel.Children.Add(Big(timer.Name, 64));
        if (detail.Length > 0) panel.Children.Add(Big(detail, 32));
        panel.Children.Add(_overdue);
        panel.Children.Add(buttons);
        Content = panel;

        host.Ticked += Refresh;
        Refresh();
    }

    /// <summary>Closes for real. The window cannot be dismissed any other way.</summary>
    public void CloseForReal()
    {
        _closing = true;
        _host.Ticked -= Refresh;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closing) e.Cancel = true;
        base.OnClosing(e);
    }

    private void Refresh()
    {
        var card = BoardLogic.Card(_timer, DateTime.UtcNow);
        _overdue.Text = card.Overdue is null ? "" : $"Overdue {card.TimeText.TrimStart('+')}";
        _host.Alerts.CloseResolvedOverlays();
    }

    private static TextBlock Big(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(40, 6, 40, 6),
    };
}

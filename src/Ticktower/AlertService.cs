using System.Media;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using Ticktower.Core.Engine;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>
/// Basic P1 alert path: a toast with Acknowledge and Snooze buttons, plus a system sound.
/// Escalation, other channels and AUMID registration arrive in P2.
/// </summary>
public sealed class AlertService : IAlertSink
{
    public static readonly TimeSpan SnoozeLength = TimeSpan.FromMinutes(5);

    private readonly Action<Guid, string> _onAction;

    public AlertService(Action<Guid, string> onAction)
    {
        _onAction = onAction;
        ToastNotificationManagerCompat.OnActivated += args =>
        {
            var parsed = ToastArguments.Parse(args.Argument);
            if (!parsed.TryGetValue("timer", out var id) || !Guid.TryParse(id, out var guid)) return;
            parsed.TryGetValue("action", out var action);
            Application.Current.Dispatcher.BeginInvoke(() => _onAction(guid, action ?? ""));
        };
    }

    /// <summary>Silences sound. Toasts still show.</summary>
    public bool Muted { get; set; }

    public void Expired(TimerItem timer, bool missed)
    {
        var title = missed ? "Missed while away" : "Timer expired";
        var detail = string.Join(" · ", new[] { timer.Ticket, timer.Client }.Where(s => !string.IsNullOrWhiteSpace(s)));

        try
        {
            var toast = new ToastContentBuilder()
                .AddArgument("timer", timer.Id.ToString())
                .AddText($"{title}: {timer.Name}");
            if (detail.Length > 0) toast.AddText(detail);
            toast
                .AddButton(new ToastButton().SetContent("Acknowledge").AddArgument("action", "ack").SetBackgroundActivation())
                .AddButton(new ToastButton().SetContent($"Snooze {(int)SnoozeLength.TotalMinutes} min").AddArgument("action", "snooze").SetBackgroundActivation())
                .SetToastScenario(ToastScenario.Reminder)
                .Show();
        }
        catch (Exception)
        {
            // A failed toast must never stop the sound or the engine.
        }

        if (!Muted) SystemSounds.Exclamation.Play();
    }
}

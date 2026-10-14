using System.IO;
using System.Media;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Toolkit.Uwp.Notifications;
using Ticktower.Core.Engine;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>
/// Delivers one rung of an escalation ladder: toast, sound, taskbar flash, full-screen overlay and TTS.
/// Each channel is isolated, so one failing never stops the others or the engine.
/// </summary>
public sealed class AlertService : IAlertSink, IDisposable
{
    private readonly AppHost _host;
    private readonly Dictionary<Guid, OverlayWindow> _overlays = new();
    private MediaPlayer? _player;
    private SpeechSynthesizer? _speech;

    public AlertService(AppHost host)
    {
        _host = host;
        ToastNotificationManagerCompat.OnActivated += args =>
        {
            var parsed = ToastArguments.Parse(args.Argument);
            if (!parsed.TryGetValue("timer", out var id) || !Guid.TryParse(id, out var guid)) return;
            parsed.TryGetValue("action", out var action);
            Application.Current.Dispatcher.BeginInvoke(() => _host.AcknowledgeOrSnooze(guid, action ?? ""));
        };
    }

    /// <summary>Silences sound and speech. Toasts, flash and overlay still show.</summary>
    public bool Muted { get; set; }

    /// <summary>Set by the app shell: flashes the taskbar button, showing the board first if it is hidden.</summary>
    public Action? FlashTaskbar { get; set; }

    public void Alert(AlertNotice notice)
    {
        var channels = notice.Step.Channels;
        if (Muted) channels &= ~(AlertChannels.Sound | AlertChannels.Tts);

        Run(channels, AlertChannels.Toast, () => ShowToast(notice));
        Run(channels, AlertChannels.Sound, () => PlaySound(notice.Alert));
        Run(channels, AlertChannels.TaskbarFlash, () => FlashTaskbar?.Invoke());
        Run(channels, AlertChannels.Overlay, () => ShowOverlay(notice.Timer));
        Run(channels, AlertChannels.Tts, () => Speak(notice));
    }

    /// <summary>Closes overlays whose timer has been acknowledged, snoozed or deleted. Call each tick.</summary>
    public void CloseResolvedOverlays()
    {
        foreach (var (id, window) in _overlays.ToList())
        {
            var timer = _host.Engine.Timers.FirstOrDefault(t => t.Id == id);
            if (timer is { State: TimerState.Expired }) continue;
            _overlays.Remove(id);
            window.CloseForReal();
        }
    }

    public void Dispose()
    {
        foreach (var w in _overlays.Values) w.CloseForReal();
        _overlays.Clear();
        _speech?.Dispose();
        _player?.Close();
    }

    private static void Run(AlertChannels all, AlertChannels channel, Action action)
    {
        if (!all.HasFlag(channel)) return;
        try { action(); }
        catch (Exception) { /* A failed channel must never stop the others or the engine. */ }
    }

    private void ShowToast(AlertNotice n)
    {
        var timer = n.Timer;
        var title = n.Missed ? "Missed while away" : n.StepIndex > 0 ? "Still waiting" : "Timer expired";
        var detail = string.Join(" · ", new[] { timer.Ticket, timer.Client }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var snooze = SnoozeText();

        var toast = new ToastContentBuilder()
            .AddArgument("timer", timer.Id.ToString())
            .AddText($"{title}: {timer.Name}");
        if (detail.Length > 0) toast.AddText(detail);
        toast
            .AddButton(new ToastButton().SetContent("Acknowledge").AddArgument("action", "ack").SetBackgroundActivation())
            .AddButton(new ToastButton().SetContent($"Snooze {snooze}").AddArgument("action", "snooze").SetBackgroundActivation())
            .SetToastScenario(ToastScenario.Reminder)
            .Show(t =>
            {
                t.Tag = timer.Id.ToString("N");   // a repeat replaces the previous toast instead of stacking
                t.Group = "ticktower";
            });
    }

    private string SnoozeText() => Core.Models.SnoozeParser.Format(_host.Settings.QuickSnooze);

    private void PlaySound(EffectiveAlert alert)
    {
        if (!string.IsNullOrWhiteSpace(alert.SoundFile) && File.Exists(alert.SoundFile))
        {
            _player ??= new MediaPlayer();
            _player.Open(new Uri(alert.SoundFile));
            _player.Volume = alert.Volume / 100.0;
            _player.Play();
        }
        else
        {
            // The system sound has no volume control; the profile volume applies to files and speech.
            SystemSounds.Exclamation.Play();
        }
    }

    private void ShowOverlay(TimerItem timer)
    {
        if (_overlays.ContainsKey(timer.Id)) return;
        var window = new OverlayWindow(_host, timer);
        _overlays[timer.Id] = window;
        window.Show();
    }

    private void Speak(AlertNotice n)
    {
        _speech ??= new SpeechSynthesizer();
        _speech.Volume = n.Alert.Volume;
        _speech.SpeakAsync(AlertText.Format(n.Alert.TtsTemplate, n.Timer));
    }

    /// <summary>Flashes the given window's taskbar button.</summary>
    public static void Flash(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        NativeMethods.Flash(hwnd);
    }
}

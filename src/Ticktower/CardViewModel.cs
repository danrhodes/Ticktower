using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Ticktower.Core.Engine;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>Presentation wrapper for one timer. All values come from <see cref="BoardLogic"/>.</summary>
public sealed class CardViewModel : INotifyPropertyChanged
{
    private readonly AppHost _host;
    private CardInfo _info;

    public CardViewModel(TimerItem timer, AppHost host)
    {
        Timer = timer;
        _host = host;
        _info = BoardLogic.Card(timer, DateTime.UtcNow);

        StartCommand = new RelayCommand(() => AppHost.Safe(() =>
        {
            if (timer.State == TimerState.Pending) host.Engine.Start(timer.Id);
            else host.Engine.Resume(timer.Id);
        }));
        PauseCommand = new RelayCommand(() => AppHost.Safe(() => host.Engine.Pause(timer.Id)));
        StopCommand = new RelayCommand(() => AppHost.Safe(() => host.Engine.Stop(timer.Id)));
        LapCommand = new RelayCommand(() => AppHost.Safe(() => host.Engine.Lap(timer.Id)));
        BookmarkCommand = new RelayCommand(Bookmark);
        SnoozeCommand = new RelayCommand(ShowSnoozeMenu);
        AcknowledgeCommand = new RelayCommand(() => AppHost.Safe(() => host.Engine.Acknowledge(timer.Id)));
        EditCommand = new RelayCommand(() => new EntryWindow(host, editing: timer).Show());
        DeleteCommand = new RelayCommand(Delete);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TimerItem Timer { get; }

    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand LapCommand { get; }
    public ICommand BookmarkCommand { get; }
    public ICommand SnoozeCommand { get; }
    public ICommand AcknowledgeCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    public string Name => Timer.Name;
    public string Subtitle => string.Join(" · ", new[] { Timer.Ticket, Timer.Client }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string PriorityText => Timer.Priority.ToString();
    public string TimeText => _info.TimeText;

    /// <summary>Card accent, from the board's colour mode and palette.</summary>
    public Brush AccentBrush { get; private set; } = Brushes.Gray;

    public Brush PriorityBrush => Timer.Priority switch
    {
        Priority.P1 => Frozen("#C62828"),
        Priority.P2 => Frozen("#E67E22"),
        Priority.P3 => Frozen("#2E7D9E"),
        _ => Frozen("#6B7A86"),
    };

    public string Meta
    {
        get
        {
            var parts = new List<string>();
            if (Timer.Kind == TimerKind.Stopwatch) parts.Add($"Laps {_info.Laps}");
            if (_info.Bookmarks > 0) parts.Add($"Bookmarks {_info.Bookmarks}");
            if (_info.SinceClientUpdate is not null) parts.Add($"Last client update {_info.SinceClientUpdateText} ago");
            if (Timer.State == TimerState.Paused) parts.Add("Paused");
            return string.Join(" · ", parts);
        }
    }

    public bool CanStart => Timer.State == TimerState.Pending || (Timer.Kind == TimerKind.Stopwatch && Timer.State == TimerState.Paused);
    public bool CanPause => Timer.Kind == TimerKind.Stopwatch && Timer.State == TimerState.Running;
    public bool CanStop => Timer.Kind == TimerKind.Stopwatch && Timer.State is TimerState.Running or TimerState.Paused;
    public bool CanLap => CanPause;
    public bool CanAcknowledge => Timer.Kind != TimerKind.Stopwatch && Timer.State == TimerState.Expired;
    public bool CanSnooze => CanAcknowledge;

    public void Refresh(DateTime nowUtc)
    {
        _info = BoardLogic.Card(Timer, nowUtc);
        AccentBrush = BrushUtil.FromHex(
            BoardColours.For(Timer, _host.Settings.ColourMode, nowUtc, _host.Palette, _host.Types), Brushes.Gray);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <summary>Snooze presets from settings plus a custom length.</summary>
    private void ShowSnoozeMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        foreach (var minutes in _host.Settings.SnoozeMinutes)
        {
            var length = TimeSpan.FromMinutes(minutes);
            var item = new MenuItem { Header = $"Snooze {SnoozeParser.Format(length)}" };
            item.Click += (_, _) => AppHost.Safe(() => _host.Engine.Snooze(Timer.Id, length));
            menu.Items.Add(item);
        }
        var custom = new MenuItem { Header = "Custom…" };
        custom.Click += (_, _) => SnoozeCustom();
        menu.Items.Add(custom);
        menu.IsOpen = true;
    }

    private void SnoozeCustom()
    {
        var text = PromptWindow.Ask("Snooze", $"Snooze {Timer.Name} for (for example 7, 90s, 1h30m):", "10");
        if (text is null) return;
        if (!SnoozeParser.TryParse(text, out var length))
        {
            FormBuilder.Error("Use a length like 7, 90s or 1h30m (1 second to 24 hours).");
            return;
        }
        AppHost.Safe(() => _host.Engine.Snooze(Timer.Id, length));
    }

    private void Bookmark()
    {
        var note = PromptWindow.Ask("Bookmark", $"Note for {Timer.Name} (optional):");
        if (note is null) return;
        AppHost.Safe(() => _host.Engine.Bookmark(Timer.Id, string.IsNullOrWhiteSpace(note) ? null : note.Trim()));
    }

    private void Delete()
    {
        var ok = MessageBox.Show($"Delete \"{Timer.Name}\"? It is saved to history.", "Ticktower",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        if (ok) _host.Engine.Remove(Timer.Id);
    }

    private static Brush Frozen(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>Editable escalation ladder: one row per rung with channel ticks and a repeat interval.</summary>
public sealed class StepsEditor : StackPanel
{
    private static readonly (AlertChannels Flag, string Label)[] Channels =
    {
        (AlertChannels.Sound, "Sound"), (AlertChannels.Toast, "Toast"), (AlertChannels.TaskbarFlash, "Flash"),
        (AlertChannels.Overlay, "Overlay"), (AlertChannels.Tts, "TTS"),
    };

    private readonly StackPanel _rows = new();

    public StepsEditor()
    {
        Children.Add(new TextBlock
        {
            Text = "Each rung fires when the previous one is unanswered for its repeat time. The last rung keeps repeating.",
            TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray, FontSize = 11,
        });
        Children.Add(_rows);
        var add = new Button { Content = "Add rung", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 4, 0, 0) };
        add.Click += (_, _) => AddRow(new AlertStep(AlertChannels.Toast, TimeSpan.FromSeconds(60)));
        Children.Add(add);
    }

    public void SetSteps(IEnumerable<AlertStep> steps)
    {
        _rows.Children.Clear();
        foreach (var s in steps) AddRow(s);
    }

    public bool TryGetSteps(out List<AlertStep> steps, out string? error)
    {
        steps = new List<AlertStep>();
        error = null;
        var n = 1;
        foreach (StackPanel row in _rows.Children)
        {
            var channels = AlertChannels.None;
            foreach (var cb in row.Children.OfType<CheckBox>())
                if (cb.IsChecked == true) channels |= (AlertChannels)cb.Tag;
            var box = row.Children.OfType<TextBox>().Single();
            if (channels == AlertChannels.None)
            {
                error = $"Rung {n} has no channel ticked.";
                return false;
            }
            if (!int.TryParse(box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds < 1 || seconds > 86400)
            {
                error = $"Rung {n}: repeat time must be 1 to 86400 seconds.";
                return false;
            }
            steps.Add(new AlertStep(channels, TimeSpan.FromSeconds(seconds)));
            n++;
        }
        if (steps.Count == 0)
        {
            error = "Add at least one rung.";
            return false;
        }
        return true;
    }

    private void AddRow(AlertStep step)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        foreach (var (flag, label) in Channels)
            row.Children.Add(new CheckBox
            {
                Content = label, Tag = flag, IsChecked = step.Channels.HasFlag(flag),
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            });
        row.Children.Add(new TextBlock { Text = "repeat after", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) });
        row.Children.Add(new TextBox { Text = ((int)step.RepeatInterval.TotalSeconds).ToString(CultureInfo.InvariantCulture), Width = 48, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = "s", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 8, 0) });
        var remove = new Button { Content = "Remove", Padding = new Thickness(6, 1, 6, 1) };
        remove.Click += (_, _) => _rows.Children.Remove(row);
        row.Children.Add(remove);
        _rows.Children.Add(row);
    }
}

/// <summary>
/// The four alert fields (ladder, sound file, volume, speech text). In override mode each field has a tick;
/// an unticked field reads back as null, meaning "inherit from the profile".
/// </summary>
public sealed class AlertFieldsPanel : Grid
{
    private readonly bool _override;
    private readonly CheckBox? _useSteps, _useSound, _useVolume, _useTts;
    private readonly StepsEditor _steps = new();
    private readonly TextBox _sound = new();
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, Width = 160, TickFrequency = 10, IsSnapToTickEnabled = true };
    private readonly TextBlock _volumeText = new() { Width = 36, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _tts = new();

    public AlertFieldsPanel(bool overrideMode)
    {
        _override = overrideMode;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _useSteps = Tick("Ladder");
        _useSound = Tick("Sound file");
        _useVolume = Tick("Volume");
        _useTts = Tick("Speech text");

        var browse = new Button { Content = "Browse…", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(4, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "Audio|*.wav;*.mp3;*.wma|All files|*.*" };
            if (dlg.ShowDialog() == true) _sound.Text = dlg.FileName;
        };
        var soundRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        soundRow.Children.Add(browse);
        soundRow.Children.Add(_sound);
        _sound.ToolTip = "Leave blank for the Windows alert sound. Volume applies to files and speech.";

        var volumeRow = new StackPanel { Orientation = Orientation.Horizontal };
        _volume.ValueChanged += (_, _) => _volumeText.Text = $"{(int)_volume.Value}%";
        volumeRow.Children.Add(_volume);
        volumeRow.Children.Add(_volumeText);

        _tts.ToolTip = "Placeholders: {name} {ticket} {client} {priority}";

        Add(_useSteps, "Ladder", _steps);
        Add(_useSound, "Sound file", soundRow);
        Add(_useVolume, "Volume", volumeRow);
        Add(_useTts, "Speech text", _tts);
        Load(null, null, null, null);
    }

    public void Load(List<AlertStep>? steps, string? sound, int? volume, string? tts)
    {
        SetField(_useSteps, steps is { Count: > 0 }, () => _steps.SetSteps(steps is { Count: > 0 } ? steps : BuiltInAlertProfiles.Create()[0].Steps));
        SetField(_useSound, sound is not null, () => _sound.Text = sound ?? "");
        SetField(_useVolume, volume is not null, () => _volume.Value = volume ?? 80);
        SetField(_useTts, tts is not null, () => _tts.Text = tts ?? AlertProfile.DefaultTtsTemplate);
        _volumeText.Text = $"{(int)_volume.Value}%";
    }

    /// <summary>Reads the fields. In override mode an unticked field comes back null.</summary>
    public bool TryRead(out List<AlertStep>? steps, out string? sound, out int? volume, out string? tts, out string? error)
    {
        steps = null; sound = null; volume = null; tts = null; error = null;

        if (On(_useSteps))
        {
            if (!_steps.TryGetSteps(out var list, out error)) return false;
            steps = list;
        }
        if (On(_useSound)) sound = string.IsNullOrWhiteSpace(_sound.Text) ? null : _sound.Text.Trim();
        if (On(_useVolume)) volume = (int)_volume.Value;
        if (On(_useTts)) tts = _tts.Text;
        return true;
    }

    private bool On(CheckBox? tick) => !_override || tick?.IsChecked == true;

    private CheckBox? Tick(string label) => _override ? new CheckBox { Content = label, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 8, 0) } : null;

    private void Add(CheckBox? tick, string label, FrameworkElement field)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var row = RowDefinitions.Count - 1;
        UIElement left = tick is not null
            ? tick
            : new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 8, 0) };
        SetRow(left, row);
        Children.Add(left);
        field.Margin = new Thickness(0, 3, 0, 3);
        SetRow(field, row);
        SetColumn(field, 1);
        Children.Add(field);
        if (tick is not null) tick.Checked += (_, _) => field.IsEnabled = true;
        if (tick is not null) tick.Unchecked += (_, _) => field.IsEnabled = false;
    }

    private void SetField(CheckBox? tick, bool on, Action fill)
    {
        fill();
        if (tick is null) return;
        tick.IsChecked = on;
        // Checked/Unchecked handlers do not fire when the value is already equal, so set enablement directly.
        foreach (UIElement child in Children)
            if (GetRow(child) == GetRow(tick) && GetColumn(child) == 1 && child is FrameworkElement fe)
                fe.IsEnabled = on;
    }
}

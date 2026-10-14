using System.Windows;
using System.Windows.Controls;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>Creates a timer from a type or preset, or edits the details of an existing one.</summary>
public sealed class EntryWindow : Window
{
    private const string NoneText = "(none)";
    private const string TypeDefaultText = "(type default)";

    private readonly AppHost _host;
    private readonly TimerItem? _editing;

    private readonly ComboBox _preset = new();
    private readonly ComboBox _type = new() { DisplayMemberPath = "Name" };
    private readonly TextBox _name = new();
    private readonly TextBox _length = new() { ToolTip = "For example 15, 90s, 1h30m" };
    private readonly TextBox _ticket = new();
    private readonly TextBox _client = new();
    private readonly ComboBox _priority = new() { ItemsSource = Enum.GetValues<Priority>() };
    private readonly ComboBox _profile = new();
    private readonly Button _overrideButton = new() { Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
    private readonly HotkeyBox _hotkey = new();
    private readonly ComboBox _chain = new();
    private readonly TextBox _notes = new() { Height = 60, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    private AlertOverride? _override;

    public EntryWindow(AppHost host, TimerType? type = null, TimerItem? editing = null, Preset? preset = null)
    {
        _host = host;
        _editing = editing;

        Title = editing is null ? "New timer" : "Edit timer";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        _type.ItemsSource = host.Types;
        _preset.ItemsSource = new object[] { NoneText }.Concat(host.Presets).ToList();
        _profile.ItemsSource = new object[] { TypeDefaultText }.Concat(host.Profiles).ToList();
        _chain.ItemsSource = new object[] { NoneText }.Concat(host.Types).ToList();
        _preset.SelectedIndex = 0;
        _chain.SelectedIndex = 0;
        _profile.SelectedIndex = 0;
        _overrideButton.Click += (_, _) => EditOverride();

        var form = new FormBuilder(90);
        if (editing is null)
        {
            form.Add("Preset", _preset);
            _preset.SelectionChanged += (_, _) => { if (_preset.SelectedItem is Preset p) Apply(p); };
        }

        var typeRow = new DockPanel();
        if (editing is null)
        {
            var newType = new Button { Content = "New type…", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
            newType.Click += (_, _) => NewType();
            DockPanel.SetDock(newType, Dock.Right);
            typeRow.Children.Add(newType);
        }
        typeRow.Children.Add(_type);
        form.Add("Type", typeRow);
        form.Add("Name", _name);
        form.Add("Length", _length);
        form.Add("Ticket", _ticket);
        form.Add("Client", _client);
        form.Add("Priority", _priority);

        var profileRow = new DockPanel();
        DockPanel.SetDock(_overrideButton, Dock.Right);
        profileRow.Children.Add(_overrideButton);
        profileRow.Children.Add(_profile);
        form.Add("Alerts", profileRow);
        form.Add("Hotkey", _hotkey);
        if (editing is null) form.Add("Then start", _chain);
        form.Add("Notes", _notes);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (editing is null)
        {
            var savePreset = new Button { Content = "Save as preset…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 14, 0) };
            savePreset.Click += (_, _) => SavePreset();
            buttons.Children.Add(savePreset);
        }
        var ok = new Button { Content = editing is null ? "Start" : "Save", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
        ok.Click += OnOk;
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        form.AddFull(buttons);
        Content = form.Grid;

        if (editing is not null) Load(editing);
        else
        {
            _type.SelectionChanged += (_, _) => OnTypeChanged();
            _type.SelectedItem = type ?? host.Types.FirstOrDefault();
            if (preset is not null) Apply(preset);
        }
        UpdateOverrideButton();
        Loaded += (_, _) => _name.Focus();
    }

    private void Load(TimerItem t)
    {
        _type.SelectedItem = _host.Types.FirstOrDefault(x => x.Id == t.TypeId);
        _type.IsEnabled = false;
        _name.Text = t.Name;
        _length.Text = t.Kind == TimerKind.Stopwatch ? "" : SnoozeParser.Format(t.Duration);
        _length.IsEnabled = false;    // a running timer's length is fixed; snooze or start a new one
        _ticket.Text = t.Ticket;
        _client.Text = t.Client;
        _priority.SelectedItem = t.Priority;
        _profile.SelectedItem = _host.Profiles.FirstOrDefault(p => p.Id == t.AlertProfileId) is { } p ? p : TypeDefaultText;
        _override = t.AlertOverride;
        _hotkey.Text = t.Hotkey ?? "";
        _notes.Text = t.Notes;
    }

    private void OnTypeChanged()
    {
        if (_type.SelectedItem is not TimerType type) return;
        _priority.SelectedItem = type.DefaultPriority;
        _length.IsEnabled = type.Kind != TimerKind.Stopwatch;
        _length.Text = type.Kind == TimerKind.Stopwatch ? "" : SnoozeParser.Format(type.DefaultDuration);
    }

    private void Apply(Preset p)
    {
        _type.SelectedItem = _host.Types.FirstOrDefault(t => t.Id == p.TypeId);
        _name.Text = p.TimerName;
        _ticket.Text = p.Ticket;
        _client.Text = p.Client;
        _priority.SelectedItem = p.Priority;
        if (p.Duration is { } d && _type.SelectedItem is TimerType { Kind: not TimerKind.Stopwatch }) _length.Text = SnoozeParser.Format(d);
        _profile.SelectedItem = _host.Profiles.FirstOrDefault(x => x.Id == p.AlertProfileId) is { } prof ? prof : TypeDefaultText;
        _override = p.AlertOverride;
        _hotkey.Text = p.Hotkey ?? "";
        _notes.Text = p.Notes;
        _chain.SelectedItem = _host.Types.FirstOrDefault(t => t.Id == p.ChainTypeId) is { } c ? c : NoneText;
        UpdateOverrideButton();
    }

    private void NewType()
    {
        var before = _host.Types.Count;
        if (new TypeEditWindow(_host).ShowDialog() != true || _host.Types.Count == before) return;
        _type.ItemsSource = null;
        _type.ItemsSource = _host.Types;
        _chain.ItemsSource = new object[] { NoneText }.Concat(_host.Types).ToList();
        _chain.SelectedIndex = 0;
        _type.SelectedItem = _host.Types[^1];
    }

    private void EditOverride()
    {
        var window = new AlertOverrideWindow(_override) { Owner = this };
        if (window.ShowDialog() != true) return;
        _override = window.Result;
        UpdateOverrideButton();
    }

    private void UpdateOverrideButton() => _overrideButton.Content = _override is null ? "Override…" : "Override ✓";

    /// <summary>Builds an unstarted timer from the form, or null (with a message shown) when the form is invalid.</summary>
    private TimerItem? Build(TimerType type)
    {
        var timer = type.CreateTimer(_name.Text.Trim(), _ticket.Text.Trim(), _client.Text.Trim(), (Priority)_priority.SelectedItem, _notes.Text);
        if (type.Kind != TimerKind.Stopwatch)
        {
            if (!SnoozeParser.TryParse(_length.Text, out var length))
            {
                FormBuilder.Error("Length must look like 15, 90s or 1h30m (1 second to 24 hours).");
                return null;
            }
            timer.Duration = length;
        }
        timer.AlertProfileId = (_profile.SelectedItem as AlertProfile)?.Id ?? type.DefaultAlertProfileId;
        timer.AlertOverride = _override;
        timer.Hotkey = string.IsNullOrWhiteSpace(_hotkey.Text) ? null : _hotkey.Text;
        return timer;
    }

    private void SavePreset()
    {
        if (_type.SelectedItem is not TimerType type) return;
        var timer = Build(type);
        if (timer is null) return;

        var suggested = string.Join(" ", new[] { _client.Text.Trim(), _priority.SelectedItem?.ToString(), type.Name }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var name = PromptWindow.Ask("Save preset", "Preset name:", suggested);
        if (string.IsNullOrWhiteSpace(name)) return;

        _host.Presets.Add(Preset.FromTimer(name.Trim(), timer, (_chain.SelectedItem as TimerType)?.Id));
        _host.SavePresets();
        _preset.ItemsSource = new object[] { NoneText }.Concat(_host.Presets).ToList();
        _preset.SelectedIndex = 0;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var hotkeyText = string.IsNullOrWhiteSpace(_hotkey.Text) ? null : _hotkey.Text;

        if (_editing is not null)
        {
            var error = _host.RegisterHotkey($"timer:{_editing.Id}", hotkeyText);
            if (error is not null) { FormBuilder.Error(error); return; }

            var profileId = (_profile.SelectedItem as AlertProfile)?.Id;
            var typeDefault = _host.Types.FirstOrDefault(t => t.Id == _editing.TypeId)?.DefaultAlertProfileId;
            _host.Engine.Edit(_editing.Id, t =>
            {
                if (!string.IsNullOrWhiteSpace(_name.Text)) t.Name = _name.Text.Trim();
                t.Ticket = _ticket.Text.Trim();
                t.Client = _client.Text.Trim();
                t.Priority = (Priority)_priority.SelectedItem;
                t.Notes = _notes.Text;
                t.AlertProfileId = profileId ?? typeDefault;
                t.AlertOverride = _override;
                t.Hotkey = hotkeyText;
            });
            Close();
            return;
        }

        if (_type.SelectedItem is not TimerType type) return;
        var timer = Build(type);
        if (timer is null) return;

        // Claim the hotkey before the timer exists so a clash is reported here, not silently dropped.
        var hkError = _host.RegisterHotkey($"timer:{timer.Id}", hotkeyText);
        if (hkError is not null) { FormBuilder.Error(hkError); return; }

        var next = (_chain.SelectedItem as TimerType)?.CreateTimer();
        _host.Engine.AddChain(timer, next);
        Close();
    }
}

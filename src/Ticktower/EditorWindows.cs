using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ticktower.Core.Engine;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>Shared shell for the modal editors: titled, topmost, sized to content, centred.</summary>
public abstract class EditorWindow : Window
{
    protected EditorWindow(string title, double width)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
    }

    protected void SetBody(FormBuilder form, Func<bool> save)
    {
        form.AddFull(FormBuilder.OkCancel(this, save));
        Content = new ScrollViewer { Content = form.Grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}

/// <summary>Create or edit a timer type. Ad hoc types are saved to types.json.</summary>
public sealed class TypeEditWindow : EditorWindow
{
    private readonly AppHost _host;
    private readonly TimerType _type;
    private readonly bool _isNew;
    private readonly TextBox _name = new();
    private readonly ComboBox _kind = new() { ItemsSource = Enum.GetValues<TimerKind>() };
    private readonly TextBox _length = new() { ToolTip = "For example 15, 90s, 1h30m" };
    private readonly ComboBox _profile = new() { DisplayMemberPath = "Name" };
    private readonly ComboBox _priority = new() { ItemsSource = Enum.GetValues<Priority>() };
    private readonly TextBox _colour = new();
    private readonly Border _swatch = new() { Width = 22, Height = 22, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(6, 0, 0, 0) };
    private readonly HotkeyBox _hotkey = new();

    public TypeEditWindow(AppHost host, TimerType? existing = null) : base(existing is null ? "New type" : "Edit type", 380)
    {
        _host = host;
        _isNew = existing is null;
        _type = existing ?? new TimerType { Id = "custom-" + Guid.NewGuid().ToString("N")[..8], Kind = TimerKind.Countdown, DefaultDuration = TimeSpan.FromMinutes(15) };

        _profile.ItemsSource = host.Profiles;
        _name.Text = _type.Name;
        _kind.SelectedItem = _type.Kind;
        _length.Text = SnoozeParser.Format(_type.DefaultDuration == TimeSpan.Zero ? TimeSpan.FromMinutes(15) : _type.DefaultDuration);
        _profile.SelectedItem = host.Profiles.FirstOrDefault(p => p.Id == _type.DefaultAlertProfileId) ?? host.Profiles.FirstOrDefault(p => p.Id == BuiltInAlertProfiles.DefaultId);
        _priority.SelectedItem = _type.DefaultPriority;
        _colour.Text = _type.Colour;
        _hotkey.Text = _type.Hotkey ?? "";
        _kind.SelectionChanged += (_, _) => _length.IsEnabled = _kind.SelectedItem is not TimerKind.Stopwatch;
        _length.IsEnabled = _type.Kind != TimerKind.Stopwatch;
        _colour.TextChanged += (_, _) => _swatch.Background = BrushUtil.FromHex(_colour.Text, Brushes.Transparent);
        _swatch.Background = BrushUtil.FromHex(_type.Colour, Brushes.Transparent);
        ColourPicker.Attach(_swatch, _colour);

        var colourRow = new DockPanel();
        DockPanel.SetDock(_swatch, Dock.Right);
        colourRow.Children.Add(_swatch);
        colourRow.Children.Add(_colour);

        var form = new FormBuilder();
        form.Add("Name", _name);
        form.Add("Kind", _kind).IsEnabled = _isNew || !_type.BuiltIn;
        form.Add("Length", _length);
        form.Add("Alert profile", _profile);
        form.Add("Priority", _priority);
        form.Add("Colour", colourRow);
        form.Add("Hotkey", _hotkey);
        SetBody(form, Save);
    }

    private bool Save()
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) { FormBuilder.Error("Give the type a name."); return false; }
        if (!ColourUtil.TryParseHex(_colour.Text.Trim(), out _)) { FormBuilder.Error("Colour must be hex like #2E9E5B."); return false; }

        var kind = (TimerKind)_kind.SelectedItem;
        var length = TimeSpan.Zero;
        if (kind != TimerKind.Stopwatch && !SnoozeParser.TryParse(_length.Text, out length))
        {
            FormBuilder.Error("Length must look like 15, 90s or 1h30m (1 second to 24 hours).");
            return false;
        }

        var owner = $"type:{_type.Id}";
        var error = _host.RegisterHotkey(owner, _hotkey.Text);
        if (error is not null) { FormBuilder.Error(error); return false; }

        _type.Name = _name.Text.Trim();
        _type.Kind = kind;
        _type.DefaultDuration = length;
        _type.DefaultAlertProfileId = (_profile.SelectedItem as AlertProfile)?.Id;
        _type.DefaultPriority = (Priority)_priority.SelectedItem;
        _type.Colour = _colour.Text.Trim().ToUpperInvariant();
        _type.Hotkey = string.IsNullOrWhiteSpace(_hotkey.Text) ? null : _hotkey.Text;
        if (_isNew) _host.Types.Add(_type);
        _host.SaveTypes();
        return true;
    }
}

/// <summary>Edit an alert profile: name plus the ladder, sound, volume and speech fields.</summary>
public sealed class AlertProfileWindow : EditorWindow
{
    private readonly AppHost _host;
    private readonly AlertProfile _profile;
    private readonly bool _isNew;
    private readonly TextBox _name = new();
    private readonly AlertFieldsPanel _fields = new(overrideMode: false);

    public AlertProfileWindow(AppHost host, AlertProfile? existing = null) : base(existing is null ? "New alert profile" : "Edit alert profile", 640)
    {
        _host = host;
        _isNew = existing is null;
        _profile = existing ?? new AlertProfile { Id = "profile-" + Guid.NewGuid().ToString("N")[..8], Name = "" };

        _name.Text = _profile.Name;
        _fields.Load(_isNew ? BuiltInAlertProfiles.Create()[0].Steps : _profile.Steps, _profile.SoundFile, _profile.Volume, _profile.TtsTemplate);

        var form = new FormBuilder();
        form.Add("Name", _name);
        form.AddFull(_fields);
        SetBody(form, Save);
    }

    private bool Save()
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) { FormBuilder.Error("Give the profile a name."); return false; }
        if (!_fields.TryRead(out var steps, out var sound, out var volume, out var tts, out var error)) { FormBuilder.Error(error!); return false; }

        _profile.Name = _name.Text.Trim();
        _profile.Steps = steps!;
        _profile.SoundFile = sound;
        _profile.Volume = volume!.Value;
        _profile.TtsTemplate = string.IsNullOrWhiteSpace(tts) ? AlertProfile.DefaultTtsTemplate : tts;
        if (_isNew) _host.Profiles.Add(_profile);
        _host.SaveProfiles();
        return true;
    }
}

/// <summary>Per-timer override of any alert profile field. Result is null when nothing is overridden.</summary>
public sealed class AlertOverrideWindow : EditorWindow
{
    private readonly AlertFieldsPanel _fields = new(overrideMode: true);

    public AlertOverrideWindow(AlertOverride? current) : base("Alert override for this timer", 640)
    {
        _fields.Load(current?.Steps, current?.SoundFile, current?.Volume, current?.TtsTemplate);
        var form = new FormBuilder();
        form.AddFull(new TextBlock { Text = "Tick a field to override it for this timer. Unticked fields come from the alert profile.", Margin = new Thickness(0, 0, 0, 8) });
        form.AddFull(_fields);
        SetBody(form, Save);
    }

    public AlertOverride? Result { get; private set; }

    private bool Save()
    {
        if (!_fields.TryRead(out var steps, out var sound, out var volume, out var tts, out var error)) { FormBuilder.Error(error!); return false; }
        var o = new AlertOverride { Steps = steps, SoundFile = sound, Volume = volume, TtsTemplate = tts };
        Result = o.IsEmpty ? null : o;
        return true;
    }
}

/// <summary>Edit a board colour palette. Every colour is a hex box with a live swatch.</summary>
public sealed class PaletteWindow : EditorWindow
{
    private readonly AppHost _host;
    private readonly ColourPalette _palette;
    private readonly bool _isNew;
    private readonly TextBox _name = new();
    private readonly List<(string Label, TextBox Box)> _fixed = new();
    private readonly StackPanel _clientRows = new();
    private readonly FormBuilder _form = new();

    public PaletteWindow(AppHost host, ColourPalette? existing = null) : base(existing is null ? "New palette" : "Edit palette", 380)
    {
        _host = host;
        _isNew = existing is null;
        _palette = existing ?? new ColourPalette { Id = "palette-" + Guid.NewGuid().ToString("N")[..8] };
        var src = _palette;

        _name.Text = src.Name;
        _form.Add("Name", _name);
        FixedColour("Urgency: plenty", src.UrgencyGreen);
        FixedColour("Urgency: closing", src.UrgencyAmber);
        FixedColour("Urgency: due", src.UrgencyRed);
        for (var i = 0; i < 4; i++) FixedColour($"Priority P{i + 1}", i < src.Priority.Count ? src.Priority[i] : src.Neutral);
        FixedColour("Neutral", src.Neutral);
        FixedColour("Done", src.Done);

        _form.Add("Clients", _clientRows);
        foreach (var c in src.Client) AddClientRow(c);
        var add = new Button { Content = "Add client colour", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2) };
        add.Click += (_, _) => AddClientRow("#808080");
        _form.AddFull(add);
        SetBody(_form, Save);
    }

    private void FixedColour(string label, string hex)
    {
        var box = new TextBox { Text = hex };
        _form.Add(label, WithSwatch(box));
        _fixed.Add((label, box));
    }

    private void AddClientRow(string hex)
    {
        var box = new TextBox { Text = hex, Width = 100 };
        var remove = new Button { Content = "Remove", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(6, 0, 0, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2), Tag = box };
        row.Children.Add(box);
        row.Children.Add(Swatch(box));
        row.Children.Add(remove);
        remove.Click += (_, _) => _clientRows.Children.Remove(row);
        _clientRows.Children.Add(row);
    }

    private static UIElement WithSwatch(TextBox box)
    {
        var row = new DockPanel();
        var swatch = Swatch(box);
        DockPanel.SetDock(swatch, Dock.Right);
        row.Children.Add(swatch);
        row.Children.Add(box);
        return row;
    }

    private static Border Swatch(TextBox box)
    {
        var swatch = new Border { Width = 22, Height = 22, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(6, 0, 0, 0), Background = BrushUtil.FromHex(box.Text, Brushes.Transparent) };
        box.TextChanged += (_, _) => swatch.Background = BrushUtil.FromHex(box.Text, Brushes.Transparent);
        ColourPicker.Attach(swatch, box);
        return swatch;
    }

    private bool Save()
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) { FormBuilder.Error("Give the palette a name."); return false; }

        var all = _fixed.Select(f => (f.Label, Hex: f.Box.Text.Trim())).ToList();
        var clients = _clientRows.Children.OfType<StackPanel>().Select(r => ((TextBox)r.Tag).Text.Trim()).ToList();
        if (clients.Count == 0) { FormBuilder.Error("Keep at least one client colour."); return false; }
        foreach (var (label, hex) in all)
            if (!ColourUtil.TryParseHex(hex, out _)) { FormBuilder.Error($"{label}: \"{hex}\" is not hex like #2E9E5B."); return false; }
        if (clients.FirstOrDefault(c => !ColourUtil.TryParseHex(c, out _)) is { } badClient)
        {
            FormBuilder.Error($"Client colour \"{badClient}\" is not hex like #2E9E5B.");
            return false;
        }

        string H(int i) => all[i].Hex.ToUpperInvariant();
        _palette.Name = _name.Text.Trim();
        _palette.UrgencyGreen = H(0);
        _palette.UrgencyAmber = H(1);
        _palette.UrgencyRed = H(2);
        _palette.Priority = new List<string> { H(3), H(4), H(5), H(6) };
        _palette.Neutral = H(7);
        _palette.Done = H(8);
        _palette.Client = clients.Select(c => c.ToUpperInvariant()).ToList();
        if (_isNew) _host.Palettes.Add(_palette);
        _host.SavePalettes();
        return true;
    }
}

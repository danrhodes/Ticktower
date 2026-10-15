using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>List with New, Edit, Delete, Import and Export for one kind of catalog item.</summary>
public sealed class CatalogPanel<T> : DockPanel where T : class, ICatalogItem
{
    private readonly string _kind;
    private readonly Func<List<T>> _items;
    private readonly Action _save;
    private readonly Func<T?, bool> _edit;
    private readonly Action<T>? _onDelete;
    private readonly Action<T>? _sanitizeImport;
    private readonly ListBox _list = new() { DisplayMemberPath = nameof(ICatalogItem.Name), MinHeight = 200 };

    /// <param name="edit">Opens the editor for an item, or a new one when null. Returns true if saved.</param>
    public CatalogPanel(string kind, Func<List<T>> items, Action save, Func<T?, bool> edit,
        Action<T>? onDelete = null, Action<T>? sanitizeImport = null)
    {
        _kind = kind;
        _items = items;
        _save = save;
        _edit = edit;
        _onDelete = onDelete;
        _sanitizeImport = sanitizeImport;
        Margin = new Thickness(8);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Button("New", () => { if (_edit(null)) Refresh(); }));
        buttons.Children.Add(Button("Edit", EditSelected));
        buttons.Children.Add(Button("Delete", DeleteSelected));
        buttons.Children.Add(Button("Import…", Import));
        buttons.Children.Add(Button("Export…", Export));
        SetDock(buttons, Dock.Bottom);
        Children.Add(buttons);
        Children.Add(_list);

        _list.MouseDoubleClick += (_, _) => EditSelected();
        Refresh();
    }

    public void Refresh()
    {
        _list.ItemsSource = null;
        _list.ItemsSource = _items().Select(i => new Row(i)).ToList();
        _list.DisplayMemberPath = nameof(Row.Label);
    }

    private sealed record Row(T Item)
    {
        public string Label => Item.BuiltIn ? $"{Item.Name}  (built-in)" : Item.Name;
    }

    private T? Selected => (_list.SelectedItem as Row)?.Item;

    private static Button Button(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    private void EditSelected()
    {
        if (Selected is { } item && _edit(item)) Refresh();
    }

    private void DeleteSelected()
    {
        if (Selected is not { } item) return;
        if (item.BuiltIn)
        {
            FormBuilder.Error("Built-in items can be edited but not deleted.");
            return;
        }
        if (MessageBox.Show($"Delete \"{item.Name}\"?", "Ticktower", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _items().Remove(item);
        _onDelete?.Invoke(item);
        _save();
        Refresh();
    }

    private void Import()
    {
        var dlg = new OpenFileDialog { Filter = "Ticktower export (*.json)|*.json", Title = $"Import {_kind}" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var incoming = CatalogIO.Read<T>(dlg.FileName, _kind);
            if (_sanitizeImport is not null) foreach (var i in incoming) _sanitizeImport(i);
            var result = CatalogIO.Merge(_items(), incoming);
            _save();
            Refresh();
            MessageBox.Show($"Imported {result.Added + result.Replaced} {_kind}: {result.Added} added, {result.Replaced} replaced.", "Ticktower");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            FormBuilder.Error($"Import failed: {ex.Message}");
        }
    }

    private void Export()
    {
        var mine = _items().Where(i => !i.BuiltIn).ToList();
        if (mine.Count == 0)
        {
            FormBuilder.Error($"There are no custom {_kind} to export. Built-ins ship with Ticktower.");
            return;
        }
        var dlg = new SaveFileDialog { Filter = "Ticktower export (*.json)|*.json", FileName = $"ticktower-{_kind}.json", Title = $"Export {_kind}" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            CatalogIO.Export(dlg.FileName, _kind, mine);
            MessageBox.Show($"Exported {mine.Count} {_kind}.", "Ticktower");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FormBuilder.Error($"Export failed: {ex.Message}");
        }
    }
}

/// <summary>Types, alert profiles, presets, palettes and general settings in one place (tray: Settings).</summary>
public sealed class LibraryWindow : Window
{
    private readonly AppHost _host;
    private readonly TextBox _snooze = new();
    private readonly ComboBox _theme = new();
    private readonly CheckBox _updates = new() { Content = "Check GitHub for a newer version on start and daily" };
    private readonly ComboBox _palette = new() { DisplayMemberPath = "Name" };

    public LibraryWindow(AppHost host)
    {
        _host = host;
        Title = "Ticktower settings";
        Width = 600;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem
        {
            Header = "Types",
            Content = new CatalogPanel<TimerType>("types", () => host.Types, host.SaveTypes,
                t => new TypeEditWindow(host, t).ShowDialog() == true,
                onDelete: t => host.Hotkeys?.Unregister($"type:{t.Id}"),
                sanitizeImport: t => t.Hotkey = null),
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Alert profiles",
            Content = new CatalogPanel<AlertProfile>("alertprofiles", () => host.Profiles, host.SaveProfiles,
                p => new AlertProfileWindow(host, p).ShowDialog() == true),
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Presets",
            Content = new CatalogPanel<Preset>("presets", () => host.Presets, host.SavePresets, Rename,
                sanitizeImport: p => p.Hotkey = null),
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Palettes",
            Content = new CatalogPanel<ColourPalette>("palettes", () => host.Palettes, host.SavePalettes,
                p => new PaletteWindow(host, p).ShowDialog() == true),
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Export profiles",
            Content = new CatalogPanel<ExportProfile>("exportprofiles", () => host.ExportProfiles, host.SaveExportProfiles,
                p => new ExportProfileWindow(host, p).ShowDialog() == true),
        });
        tabs.Items.Add(new TabItem { Header = "General", Content = BuildGeneral() });
        Content = tabs;
    }

    private bool Rename(Preset? preset)
    {
        if (preset is null)
        {
            FormBuilder.Error("Create a preset from the entry window: fill in a timer and choose Save as preset.");
            return false;
        }
        var name = PromptWindow.Ask("Rename preset", "Preset name:", preset.Name);
        if (string.IsNullOrWhiteSpace(name)) return false;
        preset.Name = name.Trim();
        _host.SavePresets();
        return true;
    }

    private UIElement BuildGeneral()
    {
        _snooze.Text = string.Join(", ", _host.Settings.SnoozeMinutes);
        _snooze.ToolTip = "Whole minutes, comma separated. The first is the one-click snooze on toasts and the overlay.";
        _palette.ItemsSource = _host.Palettes;
        _palette.SelectedItem = _host.Palette;

        var form = new FormBuilder(140);
        form.Add("Snooze presets (min)", _snooze);
        form.Add("Board palette", _palette);
        _theme.ItemsSource = Enum.GetValues<ThemeMode>();
        _theme.SelectedItem = _host.Settings.Theme;
        _theme.ToolTip = "System follows the Windows app mode. Applies straight away.";
        _theme.SelectionChanged += (_, _) =>
        {
            if (_theme.SelectedItem is not ThemeMode mode || mode == _host.Settings.Theme) return;
            _host.Settings.Theme = mode;
            _host.PersistSettings();
            ThemeManager.Apply(mode);
        };
        form.Add("Theme", _theme);
        _updates.IsChecked = _host.Settings.CheckForUpdates;
        _updates.ToolTip = "The only network use in Ticktower. It asks GitHub Releases for the latest version and sends nothing else.";
        form.Add("Updates", _updates);
        var save = new Button { Content = "Save", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        save.Click += (_, _) => SaveGeneral();
        form.AddFull(save);
        form.AddFull(new TextBlock
        {
            Text = "Custom snooze lengths are also offered from each card's Snooze button.",
            Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
        });
        return form.Grid;
    }

    private void SaveGeneral()
    {
        var minutes = new List<int>();
        foreach (var part in _snooze.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var m) || m < 1 || m > 1440)
            {
                FormBuilder.Error($"\"{part}\" is not a whole number of minutes between 1 and 1440.");
                return;
            }
            minutes.Add(m);
        }
        if (minutes.Count == 0)
        {
            FormBuilder.Error("Enter at least one snooze length.");
            return;
        }
        _host.Settings.SnoozeMinutes = minutes;
        _host.Settings.PaletteId = (_palette.SelectedItem as ColourPalette)?.Id;
        _host.Settings.CheckForUpdates = _updates.IsChecked == true;
        _host.SaveSettings();
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Ticktower.Core.Export;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>Pick a date range and an export profile, then write the CSV.</summary>
public sealed class ExportWindow : Window
{
    private readonly AppHost _host;
    private readonly DatePicker _from = new();
    private readonly DatePicker _to = new();
    private readonly ComboBox _profile = new();
    private readonly TextBlock _summary = new() { Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap };

    public ExportWindow(AppHost host)
    {
        _host = host;
        Title = "Export CSV";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        var today = DateTime.Today;
        _from.SelectedDate = new DateTime(today.Year, today.Month, 1);
        _to.SelectedDate = today;
        _profile.ItemsSource = host.ExportProfiles;
        _profile.SelectedItem = host.ExportProfiles.FirstOrDefault(p => p.Id == BuiltInExportProfiles.GenericId) ?? host.ExportProfiles.FirstOrDefault();

        var edit = new Button { Content = "Profiles…", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
        edit.Click += (_, _) =>
        {
            ((App)Application.Current).ShowSettings();
        };
        var profileRow = new DockPanel();
        DockPanel.SetDock(edit, Dock.Right);
        profileRow.Children.Add(edit);
        profileRow.Children.Add(_profile);

        var form = new FormBuilder(70);
        form.Add("From", _from);
        form.Add("To", _to);
        form.Add("Profile", profileRow);
        form.AddFull(_summary);

        var export = new Button { Content = "Export…", IsDefault = true, Width = 90, Margin = new Thickness(0, 0, 6, 0) };
        var close = new Button { Content = "Close", IsCancel = true, Width = 80 };
        export.Click += (_, _) => DoExport();
        close.Click += (_, _) => Close();
        form.AddFull(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0),
            Children = { export, close },
        });
        Content = form.Grid;

        _from.SelectedDateChanged += (_, _) => UpdateSummary();
        _to.SelectedDateChanged += (_, _) => UpdateSummary();
        UpdateSummary();
    }

    /// <summary>Local calendar days, inclusive, as a UTC half-open range.</summary>
    private (DateTime FromUtc, DateTime ToUtc)? Range()
    {
        if (_from.SelectedDate is not { } from || _to.SelectedDate is not { } to || to < from) return null;
        var start = DateTime.SpecifyKind(from.Date, DateTimeKind.Local).ToUniversalTime();
        var end = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
        return (start, end);
    }

    private void UpdateSummary()
    {
        if (Range() is not { } r)
        {
            _summary.Text = "Choose a start date on or before the end date.";
            return;
        }
        var count = _host.History.ReadRange(r.FromUtc, r.ToUtc).Count;
        _summary.Text = $"{count} closed timer{(count == 1 ? "" : "s")} in this range. A timer is added to history when you delete it from the board.";
    }

    private void DoExport()
    {
        if (Range() is not { } r) { FormBuilder.Error("Choose a start date on or before the end date."); return; }
        if (_profile.SelectedItem is not ExportProfile profile) { FormBuilder.Error("Choose an export profile."); return; }

        var problems = CsvExporter.Validate(profile);
        if (problems.Count > 0) { FormBuilder.Error($"This profile cannot be used yet:\n\n{string.Join("\n", problems)}"); return; }

        var records = _host.History.ReadRange(r.FromUtc, r.ToUtc);
        if (records.Count == 0) { FormBuilder.Error("There are no closed timers in that range."); return; }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"ticktower-{_from.SelectedDate:yyyyMMdd}-{_to.SelectedDate:yyyyMMdd}.csv",
            Title = "Export CSV",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            CsvExporter.WriteFile(dialog.FileName, profile, CsvExporter.Build(profile, records, _host.TypeNames));
            MessageBox.Show($"Exported {records.Count} timer{(records.Count == 1 ? "" : "s")} to\n{dialog.FileName}", "Ticktower");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FormBuilder.Error($"Export failed: {ex.Message}");
        }
    }
}

/// <summary>Rows for the export profile's columns: tick, source, header, optional format, and reordering.</summary>
public sealed class ColumnsEditor : StackPanel
{
    private readonly StackPanel _rows = new();

    public ColumnsEditor()
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        header.Children.Add(Label("On", 34));
        header.Children.Add(Label("Source", 140));
        header.Children.Add(Label("Header", 150));
        header.Children.Add(Label("Format (optional)", 120));
        Children.Add(header);
        Children.Add(_rows);

        var add = new Button { Content = "Add column", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 4, 0, 0) };
        add.Click += (_, _) => AddRow(new ExportColumn(ExportSource.Name, "New column"));
        Children.Add(add);
        Children.Add(new TextBlock
        {
            Text = "Format: dates take iso or a pattern like yyyy-MM-dd HH:mm. Duration takes hh:mm:ss, decimal-hours or minutes.",
            TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 4, 0, 0),
        });
    }

    public void SetColumns(IEnumerable<ExportColumn> columns)
    {
        _rows.Children.Clear();
        foreach (var c in columns) AddRow(c);
    }

    public List<ExportColumn> GetColumns() => _rows.Children.OfType<StackPanel>().Select(row =>
    {
        var texts = row.Children.OfType<TextBox>().ToList();
        return new ExportColumn
        {
            Enabled = row.Children.OfType<CheckBox>().Single().IsChecked == true,
            Source = (ExportSource)row.Children.OfType<ComboBox>().Single().SelectedItem,
            Header = texts[0].Text,
            Format = string.IsNullOrWhiteSpace(texts[1].Text) ? null : texts[1].Text.Trim(),
        };
    }).ToList();

    private static TextBlock Label(string text, double width) => new() { Text = text, Width = width, Foreground = System.Windows.Media.Brushes.Gray };

    private void AddRow(ExportColumn c)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new CheckBox { IsChecked = c.Enabled, Width = 34, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new ComboBox { ItemsSource = Enum.GetValues<ExportSource>(), SelectedItem = c.Source, Width = 134, Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(new TextBox { Text = c.Header, Width = 144, Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(new TextBox { Text = c.Format ?? "", Width = 114, Margin = new Thickness(0, 0, 6, 0) });

        var up = new Button { Content = "▲", Width = 24 };
        var down = new Button { Content = "▼", Width = 24, Margin = new Thickness(2, 0, 2, 0) };
        var remove = new Button { Content = "✕", Width = 24 };
        up.Click += (_, _) => Move(row, -1);
        down.Click += (_, _) => Move(row, 1);
        remove.Click += (_, _) => _rows.Children.Remove(row);
        row.Children.Add(up);
        row.Children.Add(down);
        row.Children.Add(remove);
        _rows.Children.Add(row);
    }

    private void Move(UIElement row, int delta)
    {
        var i = _rows.Children.IndexOf(row);
        var j = i + delta;
        if (j < 0 || j >= _rows.Children.Count) return;
        _rows.Children.RemoveAt(i);
        _rows.Children.Insert(j, row);
    }
}

/// <summary>Edit a CSV export profile: delimiter, quoting, encoding, row mode, formats and columns.</summary>
public sealed class ExportProfileWindow : EditorWindow
{
    private static readonly (string Label, string Value)[] Delimiters =
    {
        ("Comma ,", ","), ("Semicolon ;", ";"), ("Tab", "\t"), ("Pipe |", "|"),
    };

    private readonly AppHost _host;
    private readonly ExportProfile _profile;
    private readonly bool _isNew;
    private readonly TextBox _name = new();
    private readonly ComboBox _delimiter = new();
    private readonly ComboBox _quote = new() { ItemsSource = Enum.GetValues<CsvQuoteStyle>() };
    private readonly ComboBox _encoding = new() { ItemsSource = Enum.GetValues<CsvEncodingKind>() };
    private readonly ComboBox _rowMode = new() { ItemsSource = Enum.GetValues<ExportRowMode>() };
    private readonly CheckBox _header = new() { Content = "Include a header row" };
    private readonly TextBox _dateFormat = new() { ToolTip = "iso, or a pattern such as yyyy-MM-dd HH:mm (applied to the UTC time)" };
    private readonly ComboBox _durationFormat = new() { ItemsSource = Enum.GetValues<DurationFormat>() };
    private readonly CheckBox _protect = new() { Content = "Defuse text starting with = + - @ (stops spreadsheet formulas)" };
    private readonly ColumnsEditor _columns = new();

    public ExportProfileWindow(AppHost host, ExportProfile? existing = null) : base(existing is null ? "New export profile" : "Edit export profile", 640)
    {
        _host = host;
        _isNew = existing is null;
        var source = existing ?? BuiltInExportProfiles.Create()[0].Clone();
        _profile = existing ?? new ExportProfile { Id = "export-" + Guid.NewGuid().ToString("N")[..8] };

        _name.Text = _isNew ? "" : source.Name;
        foreach (var (label, value) in Delimiters) _delimiter.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        var current = _delimiter.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == source.Delimiter);
        if (current is null)
        {
            current = new ComboBoxItem { Content = $"Custom: {source.Delimiter}", Tag = source.Delimiter };
            _delimiter.Items.Add(current);
        }
        _delimiter.SelectedItem = current;
        _quote.SelectedItem = source.QuoteStyle;
        _encoding.SelectedItem = source.Encoding;
        _rowMode.SelectedItem = source.RowMode;
        _header.IsChecked = source.IncludeHeader;
        _dateFormat.Text = source.DateFormat;
        _durationFormat.SelectedItem = source.DurationFormat;
        _protect.IsChecked = source.ProtectFormulas;
        _columns.SetColumns(source.Columns);

        var form = new FormBuilder(110);
        form.Add("Name", _name);
        form.Add("Delimiter", _delimiter);
        form.Add("Quoting", _quote);
        form.Add("Encoding", _encoding);
        form.Add("Rows", _rowMode);
        form.Add("Date format", _dateFormat);
        form.Add("Duration", _durationFormat);
        form.AddFull(_header);
        form.AddFull(_protect);
        form.AddFull(new TextBlock { Text = "Columns", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
        form.AddFull(_columns);
        SetBody(form, Save);
    }

    private bool Save()
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) { FormBuilder.Error("Give the profile a name."); return false; }

        var draft = new ExportProfile
        {
            Delimiter = (string)((ComboBoxItem)_delimiter.SelectedItem).Tag,
            QuoteStyle = (CsvQuoteStyle)_quote.SelectedItem,
            Encoding = (CsvEncodingKind)_encoding.SelectedItem,
            RowMode = (ExportRowMode)_rowMode.SelectedItem,
            IncludeHeader = _header.IsChecked == true,
            DateFormat = _dateFormat.Text.Trim(),
            DurationFormat = (DurationFormat)_durationFormat.SelectedItem,
            ProtectFormulas = _protect.IsChecked == true,
            Columns = _columns.GetColumns(),
        };
        var problems = CsvExporter.Validate(draft);
        if (problems.Count > 0) { FormBuilder.Error(string.Join("\n", problems)); return false; }

        _profile.Name = _name.Text.Trim();
        _profile.Delimiter = draft.Delimiter;
        _profile.QuoteStyle = draft.QuoteStyle;
        _profile.Encoding = draft.Encoding;
        _profile.RowMode = draft.RowMode;
        _profile.IncludeHeader = draft.IncludeHeader;
        _profile.DateFormat = draft.DateFormat;
        _profile.DurationFormat = draft.DurationFormat;
        _profile.ProtectFormulas = draft.ProtectFormulas;
        _profile.Columns = draft.Columns;
        if (_isNew) _host.ExportProfiles.Add(_profile);
        _host.SaveExportProfiles();
        return true;
    }
}

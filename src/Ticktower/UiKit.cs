using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ticktower.Core;

namespace Ticktower;

/// <summary>Two-column label/control form, built in code so the small editor windows stay compact.</summary>
public sealed class FormBuilder
{
    public Grid Grid { get; } = new() { Margin = new Thickness(12) };

    public FormBuilder(double labelWidth = 110)
    {
        Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    }

    public T Add<T>(string label, T control) where T : UIElement
    {
        var row = NextRow();
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 6, 0) };
        Grid.SetRow(text, row);
        Grid.Children.Add(text);
        if (control is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(0, 3, 0, 3);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        Grid.Children.Add(control);
        return control;
    }

    public T AddFull<T>(T control) where T : UIElement
    {
        var row = NextRow();
        Grid.SetRow(control, row);
        Grid.SetColumnSpan(control, 2);
        Grid.Children.Add(control);
        return control;
    }

    private int NextRow()
    {
        Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return Grid.RowDefinitions.Count - 1;
    }

    /// <summary>OK and Cancel buttons for a modal window. OK runs <paramref name="ok"/>; return true from it to close.</summary>
    public static StackPanel OkCancel(Window window, Func<bool> ok, string okText = "Save")
    {
        var okButton = new Button { Content = okText, IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
        okButton.Click += (_, _) =>
        {
            if (ok()) window.DialogResult = true;
        };
        cancel.Click += (_, _) => window.DialogResult = false;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { okButton, cancel },
        };
    }

    public static void Error(string message) =>
        MessageBox.Show(message, "Ticktower", MessageBoxButton.OK, MessageBoxImage.Warning);
}

/// <summary>Read-only box that records the key combination pressed in it. Backspace or Delete clears it.</summary>
public sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        IsReadOnly = true;
        ToolTip = "Click, then press the combination (for example Ctrl+Alt+K). Backspace clears.";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;   // keep keyboard navigation
        e.Handled = true;

        if (key is Key.Back or Key.Delete)
        {
            Text = "";
            return;
        }

        string? name = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            >= Key.F1 and <= Key.F24 => key.ToString(),
            _ => null,
        };
        if (name is null) return;

        var mods = Keyboard.Modifiers;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);
        if (Hotkey.TryParse(string.Join('+', parts), out var hotkey)) Text = hotkey.ToString();
    }
}

public static class BrushUtil
{
    public static Brush FromHex(string? hex, Brush fallback)
    {
        if (!Core.Models.ColourUtil.TryParseHex(hex, out var c)) return fallback;
        var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}

public static class ColourPicker
{
    /// <summary>Makes the swatch open the Windows colour dialog; the chosen colour is written to the box as hex.</summary>
    public static void Attach(Border swatch, TextBox box)
    {
        swatch.Cursor = Cursors.Hand;
        swatch.ToolTip = "Click to choose a colour";
        swatch.MouseLeftButtonUp += (_, _) =>
        {
            using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
            if (Core.Models.ColourUtil.TryParseHex(box.Text, out var c))
                dialog.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            box.Text = Core.Models.ColourUtil.ToHex(dialog.Color.R, dialog.Color.G, dialog.Color.B);
        };
    }
}

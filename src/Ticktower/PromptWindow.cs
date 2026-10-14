using System.Windows;
using System.Windows.Controls;

namespace Ticktower;

/// <summary>Minimal modal text prompt. Returns null on cancel.</summary>
public static class PromptWindow
{
    public static string? Ask(string title, string label)
    {
        var box = new TextBox { MinWidth = 280, Margin = new Thickness(0, 6, 0, 10) };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 70, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 70 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(box);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
        };
        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) => box.Focus();
        return window.ShowDialog() == true ? box.Text : null;
    }
}

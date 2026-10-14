using System.Windows;
using System.Windows.Controls;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>Small floating window with one button per timer type. A click opens the entry window.</summary>
public partial class LauncherWindow : Window
{
    private readonly AppHost _host;

    public LauncherWindow(AppHost host)
    {
        InitializeComponent();
        _host = host;
        TypeButtons.ItemsSource = host.Types;
    }

    private void OnTypeClick(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is TimerType type)
            new EntryWindow(_host, type).Show();
    }
}

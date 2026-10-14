using System.ComponentModel;
using System.Windows;
using Ticktower.Core;

namespace Ticktower;

public partial class App : Application
{
    private SingleInstance? _single;
    private AppHost? _host;
    private LauncherWindow? _launcher;
    private BoardWindow? _board;
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _single = new SingleInstance("Ticktower.App");
        if (!_single.IsPrimary)
        {
            _single.SignalExisting();
            Shutdown();
            return;
        }
        _single.Listen(() => Dispatcher.BeginInvoke(ShowLauncher));

        _host = new AppHost();
        _host.Start();

        _launcher = new LauncherWindow(_host);
        _board = new BoardWindow(_host);
        KeepAliveInTray(_launcher);
        KeepAliveInTray(_board);
        BuildTray();

        ShowLauncher();
        if (_host.Engine.Timers.Count > 0) ShowBoard();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _host?.Stop();
        _single?.Dispose();
        base.OnExit(e);
    }

    private void ShowLauncher() => Present(_launcher);

    private void ShowBoard() => Present(_board);

    private void NewTimer() => new EntryWindow(_host!).Show();

    /// <summary>Minimise and close hide the window to the tray. Only the tray menu exits.</summary>
    private void KeepAliveInTray(Window window)
    {
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Minimized) window.Hide();
        };
        window.Closing += (_, args) =>
        {
            if (_exiting) return;
            args.Cancel = true;
            window.Hide();
        };
    }

    private static void Present(Window? window)
    {
        if (window is null) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void BuildTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show board", null, (_, _) => ShowBoard());
        menu.Items.Add("Show launcher", null, (_, _) => ShowLauncher());
        menu.Items.Add("New timer", null, (_, _) => NewTimer());
        var mute = new System.Windows.Forms.ToolStripMenuItem("Mute all") { CheckOnClick = true };
        mute.CheckedChanged += (_, _) => _host!.Alerts.Muted = mute.Checked;
        menu.Items.Add(mute);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _exiting = true;
            _tray!.Visible = false;
            Shutdown();
        });

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Ticktower",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowBoard();
    }
}

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
    private UpdateService? _updates;
    private System.Windows.Forms.ToolStripMenuItem? _updateItem;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        UpdateService.WaitForPredecessor(e.Args);

        // A TICKTOWER_DATA override is a separate sandbox, so it gets its own instance name and can run beside the real app.
        var dataOverride = Environment.GetEnvironmentVariable("TICKTOWER_DATA");
        _single = new SingleInstance(string.IsNullOrWhiteSpace(dataOverride)
            ? "Ticktower.App"
            : $"Ticktower.App.{Core.Models.BoardColours.StableHash(dataOverride.ToLowerInvariant()):X8}");
        if (!_single.IsPrimary)
        {
            _single.SignalExisting();
            Shutdown();
            return;
        }
        _single.Listen(() => Dispatcher.BeginInvoke(ShowLauncher));

        // The AUMID must be set before the first toast so notifications carry the Ticktower identity.
        NativeMethods.RegisterAumid();

        _host = new AppHost();
        _host.Start();

        _launcher = new LauncherWindow(_host);
        _board = new BoardWindow(_host);
        _board.Topmost = _host.Settings.BoardAlwaysOnTop;
        WindowPlacer.Attach(_launcher, "launcher", _host, restoreSize: false);
        WindowPlacer.Attach(_board, "board", _host, restoreSize: true);
        KeepAliveInTray(_launcher);
        KeepAliveInTray(_board);
        BuildTray();
        _host.Alerts.FlashTaskbar = FlashBoard;

        _updates = new UpdateService(_host);
        _updates.Found += OnUpdateFound;
        _updates.Start();

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

    private LibraryWindow? _settings;
    private ExportWindow? _export;

    public void ShowExport()
    {
        if (_export is { IsLoaded: true })
        {
            _export.Activate();
            return;
        }
        _export = new ExportWindow(_host!);
        _export.Show();
    }

    public void ShowSettings()
    {
        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }
        _settings = new LibraryWindow(_host!);
        _settings.Show();
    }

    private void OnUpdateFound(Core.Update.UpdateInfo update)
    {
        _updateItem!.Text = $"Update to {update.Tag}…";
        _updateItem.Visible = true;
        if (_host!.Settings.SkippedVersion == update.Tag) return;
        _tray?.ShowBalloonTip(8000, "Ticktower update", $"{update.Tag} is available. Use the tray menu to install it.",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private async Task CheckForUpdatesManually()
    {
        var (update, error) = await _updates!.CheckAsync(manual: true);
        if (error is not null) MessageBox.Show(error, "Ticktower", MessageBoxButton.OK, MessageBoxImage.Warning);
        else if (update is null) MessageBox.Show($"Ticktower {_updates.Current.ToString(3)} is the latest version.", "Ticktower");
        else await OfferUpdate(update);
    }

    private async Task OfferUpdate(Core.Update.UpdateInfo update)
    {
        var canInstall = UpdateService.SelfPath is not null;
        var text = $"Ticktower {update.Tag} is available (you have {_updates!.Current.ToString(3)}).\n\n" +
            (canInstall
                ? "Yes: download and install now. Ticktower restarts and your timers carry on.\nNo: remind me later.\nCancel: skip this version."
                : "This copy cannot update itself. Yes opens the release page.\nNo: remind me later.\nCancel: skip this version.");
        var answer = MessageBox.Show(text, "Ticktower update", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Cancel)
        {
            _updates.Skip(update);
            _updateItem!.Visible = false;
            return;
        }
        if (answer != MessageBoxResult.Yes) return;

        if (!canInstall)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(update.PageUrl) { UseShellExecute = true });
            return;
        }
        var error = await _updates.InstallAsync(update);
        if (error is not null)
        {
            MessageBox.Show(error, "Ticktower", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _exiting = true;
        _tray!.Visible = false;
        Shutdown();
    }

    /// <summary>Flashes the board's taskbar button. A hidden board is shown without taking focus so it has a button to flash.</summary>
    private void FlashBoard()
    {
        if (_board is null) return;
        if (!_board.IsVisible)
        {
            _board.ShowActivated = false;
            _board.Show();
            _board.ShowActivated = true;
        }
        AlertService.Flash(_board);
    }

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
        menu.Items.Add("Export CSV…", null, (_, _) => ShowExport());
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add("Check for updates…", null, async (_, _) => await CheckForUpdatesManually());
        _updateItem = new System.Windows.Forms.ToolStripMenuItem("Update available") { Visible = false };
        _updateItem.Click += async (_, _) => { if (_updates?.Available is { } u) await OfferUpdate(u); };
        menu.Items.Add(_updateItem);
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

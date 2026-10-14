using Ticktower.Core.Models;
using Ticktower.Core.Storage;

namespace Ticktower.Tests;

public sealed class WindowMemoryTests : IDisposable
{
    private static readonly MonitorInfo Main = new(@"\\.\DISPLAY1", true, 0, 0, 1920, 1040);
    private static readonly MonitorInfo Second = new(@"\\.\DISPLAY2", false, 1920, 0, 1280, 984);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ticktower-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static WindowPlacement At(double l, double t, double w, double h, bool max = false) =>
        new() { Left = l, Top = t, Width = w, Height = h, Maximized = max };

    [Fact]
    public void Restores_the_last_monitor_and_keeps_a_separate_spot_per_monitor()
    {
        var s = new SettingsFile();
        WindowMemoryLogic.Remember(s, "board", Main.Name, At(100, 100, 800, 600));
        WindowMemoryLogic.Remember(s, "board", Second.Name, At(2000, 50, 1000, 700, max: true));

        var (p, m) = WindowMemoryLogic.Restore(s, "board", new[] { Main, Second })!.Value;
        Assert.Equal(Second.Name, m.Name);
        Assert.Equal((2000, 50, 1000, 700, true), (p.Left, p.Top, p.Width, p.Height, p.Maximized));

        WindowMemoryLogic.Remember(s, "board", Main.Name, At(100, 100, 800, 600));   // moved back
        Assert.Equal(Main.Name, WindowMemoryLogic.Restore(s, "board", new[] { Main, Second })!.Value.Monitor.Name);
    }

    [Fact]
    public void Unplugged_monitor_falls_back_to_the_primary_spot()
    {
        var s = new SettingsFile();
        WindowMemoryLogic.Remember(s, "board", Main.Name, At(100, 100, 800, 600));
        WindowMemoryLogic.Remember(s, "board", Second.Name, At(2000, 50, 1000, 700));   // last used

        var (p, m) = WindowMemoryLogic.Restore(s, "board", new[] { Main })!.Value;
        Assert.Equal(Main.Name, m.Name);
        Assert.Equal(100, p.Left);
    }

    [Fact]
    public void Nothing_usable_means_no_restore()
    {
        var s = new SettingsFile();
        Assert.Null(WindowMemoryLogic.Restore(s, "board", new[] { Main }));

        WindowMemoryLogic.Remember(s, "board", Second.Name, At(2000, 50, 1000, 700));
        Assert.Null(WindowMemoryLogic.Restore(s, "board", new[] { Main }));      // only memory is for a monitor that is gone
        Assert.Null(WindowMemoryLogic.Restore(s, "launcher", new[] { Main, Second }));
        Assert.Null(WindowMemoryLogic.Restore(s, "board", Array.Empty<MonitorInfo>()));
    }

    [Fact]
    public void Placement_is_pulled_back_on_screen_and_shrunk_to_fit()
    {
        var s = new SettingsFile();
        WindowMemoryLogic.Remember(s, "a", Main.Name, At(1800, 1000, 400, 300));      // hangs off the corner
        var (p, _) = WindowMemoryLogic.Restore(s, "a", new[] { Main })!.Value;
        Assert.Equal((1520, 740), (p.Left, p.Top));

        WindowMemoryLogic.Remember(s, "b", Main.Name, At(-500, -200, 5000, 4000));    // bigger than the screen
        var (q, _) = WindowMemoryLogic.Restore(s, "b", new[] { Main })!.Value;
        Assert.Equal((0, 0, 1920, 1040), (q.Left, q.Top, q.Width, q.Height));
    }

    [Fact]
    public void Resolution_change_keeps_a_window_reachable()
    {
        var s = new SettingsFile();
        WindowMemoryLogic.Remember(s, "board", Main.Name, At(1500, 800, 400, 300));
        var smaller = Main with { Width = 1280, Height = 720 };
        var (p, _) = WindowMemoryLogic.Restore(s, "board", new[] { smaller })!.Value;
        Assert.True(p.Left + p.Width <= 1280 && p.Top + p.Height <= 720);
    }

    [Fact]
    public void Settings_round_trip_and_v1_files_migrate()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        var s = new SettingsFile { BoardAlwaysOnTop = true };
        WindowMemoryLogic.Remember(s, "board", Main.Name, At(10, 20, 300, 200, max: true));
        var store = new JsonFileStore();
        store.Save(path, s);
        var back = store.Load(path, () => new SettingsFile(), SettingsMigrations.Create()).Value;
        Assert.True(back.BoardAlwaysOnTop);
        Assert.True(back.Windows["board"].ByMonitor[Main.Name].Maximized);

        File.WriteAllText(path, """{"version":1,"snoozeMinutes":[7],"colourMode":"Client"}""");
        var migrated = store.Load(path, () => new SettingsFile(), SettingsMigrations.Create());
        Assert.Equal(LoadOutcome.Migrated, migrated.Outcome);
        Assert.Equal(new[] { 7 }, migrated.Value.SnoozeMinutes);
        Assert.Equal(ColourMode.Client, migrated.Value.ColourMode);
        Assert.Empty(migrated.Value.Windows);
    }
}

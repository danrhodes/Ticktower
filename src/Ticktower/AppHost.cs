using System.Windows.Threading;
using Ticktower.Core.Engine;
using Ticktower.Core.Models;
using Ticktower.Core.Storage;

namespace Ticktower;

/// <summary>Wires storage, engine, history and alerts together. Holds no timer logic of its own.</summary>
public sealed class AppHost
{
    private readonly JsonFileStore _store = new();
    private readonly Migrator _migrator = new(StateFile.CurrentVersion, Array.Empty<IMigration>());
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };

    public AppHost()
    {
        Paths = AppPaths.Default();
        History = new HistoryWriter(Paths.Root);
        Alerts = new AlertService(OnToastAction);
        Engine = new TimerEngine(Alerts);
    }

    public AppPaths Paths { get; }
    public HistoryWriter History { get; }
    public AlertService Alerts { get; }
    public TimerEngine Engine { get; }
    public List<TimerType> Types { get; private set; } = BuiltInTypes.Create();

    /// <summary>Raised every second after the engine tick, so views can refresh derived text.</summary>
    public event Action? Ticked;

    public void Start()
    {
        Types = _store.Load(Paths.Types, () => new TypesFile(), new Migrator(TypesFile.CurrentVersion, Array.Empty<IMigration>())).Value.Types;
        var state = _store.Load(Paths.State, () => new StateFile(), _migrator).Value;
        Engine.Load(state.Timers);

        Engine.Changed += () => _store.Save(Paths.State, Engine.Snapshot());
        Engine.Closed += (timer, utc) => History.Append(timer, utc);

        // Fires alerts for anything that expired while the app was closed or the PC slept.
        Engine.Recompute();

        _ticker.Tick += (_, _) =>
        {
            Engine.Tick();
            Ticked?.Invoke();
        };
        _ticker.Start();
    }

    public void Stop()
    {
        _ticker.Stop();
        _store.Save(Paths.State, Engine.Snapshot());
    }

    /// <summary>Runs an engine action, swallowing stale-UI errors such as acknowledging an already-snoozed timer.</summary>
    public static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { }
    }

    private void OnToastAction(Guid id, string action) => Safe(() =>
    {
        if (action == "ack") Engine.Acknowledge(id);
        else if (action == "snooze") Engine.Snooze(id, AlertService.SnoozeLength);
    });
}

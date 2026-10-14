using System.Windows.Threading;
using Ticktower.Core.Engine;
using Ticktower.Core.Models;
using Ticktower.Core.Storage;

namespace Ticktower;

/// <summary>Wires storage, engine, history, alerts and hotkeys together. Holds no timer logic of its own.</summary>
public sealed class AppHost
{
    private readonly JsonFileStore _store = new();
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };

    public AppHost()
    {
        Paths = AppPaths.Default();
        History = new HistoryWriter(Paths.Root);
        Alerts = new AlertService(this);
        Engine = new TimerEngine(Alerts);
        Engine.AlertSource = t => AlertResolver.Resolve(t, Profiles);
    }

    public AppPaths Paths { get; }
    public HistoryWriter History { get; }
    public AlertService Alerts { get; }
    public TimerEngine Engine { get; }
    public HotkeyService? Hotkeys { get; private set; }

    public List<TimerType> Types { get; private set; } = BuiltInTypes.Create();
    public List<AlertProfile> Profiles { get; private set; } = BuiltInAlertProfiles.Create();
    public List<ColourPalette> Palettes { get; private set; } = BuiltInPalettes.Create();
    public List<Preset> Presets { get; private set; } = new();
    public SettingsFile Settings { get; private set; } = new();

    public ColourPalette Palette =>
        Palettes.FirstOrDefault(p => p.Id == Settings.PaletteId) ?? Palettes.FirstOrDefault() ?? new ColourPalette();

    /// <summary>Raised every second after the engine tick, so views can refresh derived text.</summary>
    public event Action? Ticked;

    /// <summary>Raised when types, profiles, palettes, presets or settings change.</summary>
    public event Action? CatalogChanged;

    public void Start()
    {
        Types = Load(Paths.Types, () => new TypesFile(), TypesFile.CurrentVersion).Types;
        Profiles = Load(Paths.AlertProfiles, () => new AlertProfilesFile(), AlertProfilesFile.CurrentVersion).Profiles;
        Palettes = Load(Paths.Palettes, () => new PalettesFile(), PalettesFile.CurrentVersion).Palettes;
        Presets = Load(Paths.Presets, () => new PresetsFile(), PresetsFile.CurrentVersion).Presets;
        Settings = Load(Paths.Settings, () => new SettingsFile(), SettingsFile.CurrentVersion);
        var state = _store.Load(Paths.State, () => new StateFile(), StateMigrations.Create()).Value;
        Engine.Load(state.Timers);

        Engine.Changed += () =>
        {
            _store.Save(Paths.State, Engine.Snapshot());
            SyncTimerHotkeys();
        };
        Engine.Closed += (timer, utc) => History.Append(timer, utc);

        Hotkeys = new HotkeyService();
        Hotkeys.Pressed += OnHotkey;
        foreach (var t in Types)
            if (!string.IsNullOrWhiteSpace(t.Hotkey)) RegisterHotkey($"type:{t.Id}", t.Hotkey);
        SyncTimerHotkeys();

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
        Hotkeys?.Dispose();
        Alerts.Dispose();
    }

    public void SaveTypes() => Save(Paths.Types, new TypesFile { Types = Types });
    public void SaveProfiles() => Save(Paths.AlertProfiles, new AlertProfilesFile { Profiles = Profiles });
    public void SavePalettes() => Save(Paths.Palettes, new PalettesFile { Palettes = Palettes });
    public void SavePresets() => Save(Paths.Presets, new PresetsFile { Presets = Presets });
    public void SaveSettings() => Save(Paths.Settings, Settings);

    private void Save<T>(string path, T value)
    {
        _store.Save(path, value);
        CatalogChanged?.Invoke();
    }

    /// <summary>Runs an engine action, swallowing stale-UI errors such as acknowledging an already-snoozed timer.</summary>
    public static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { }
    }

    public void AcknowledgeOrSnooze(Guid id, string action) => Safe(() =>
    {
        if (action == "ack") Engine.Acknowledge(id);
        else if (action == "snooze") Engine.Snooze(id, Settings.QuickSnooze);
    });

    /// <summary>Starts a new timer from a preset, with its queued chain successor if it has one.</summary>
    public TimerItem? StartPreset(Preset preset)
    {
        var timer = preset.CreateTimer(Types);
        if (timer is null) return null;
        // A hotkey can belong to one timer at a time; later copies from the same preset go without.
        if (timer.Hotkey is { } hk && Engine.Timers.Any(t => t.Hotkey == hk)) timer.Hotkey = null;
        var next = preset.ChainTypeId is { } id ? Types.FirstOrDefault(t => t.Id == id)?.CreateTimer() : null;
        return Engine.AddChain(timer, next);
    }

    public string? RegisterHotkey(string ownerId, string? text)
    {
        var error = Hotkeys?.Register(ownerId, text);
        if (error is not null && Hotkeys is not null) Hotkeys.Failures[ownerId] = error;
        return error;
    }

    /// <summary>Failure messages for hotkeys that could not be registered, for the launcher status line.</summary>
    public IReadOnlyCollection<string> HotkeyFailures => Hotkeys?.Failures.Values ?? (IReadOnlyCollection<string>)Array.Empty<string>();

    private readonly Dictionary<string, string?> _attempted = new();

    /// <summary>Keeps timer hotkeys registered while their timers exist, and releases them when the timers go.</summary>
    private void SyncTimerHotkeys()
    {
        if (Hotkeys is null) return;
        var live = Engine.Timers.ToDictionary(t => $"timer:{t.Id}", t => t.Hotkey);

        foreach (var gone in _attempted.Keys.Where(k => !live.ContainsKey(k)).ToList())
        {
            Hotkeys.Unregister(gone);
            _attempted.Remove(gone);
        }
        foreach (var (owner, text) in live)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (_attempted.TryGetValue(owner, out var seen) && seen == text) continue;
            _attempted[owner] = text;
            RegisterHotkey(owner, text);
        }
    }

    private void OnHotkey(string owner) => Safe(() =>
    {
        if (owner.StartsWith("type:", StringComparison.Ordinal))
        {
            var type = Types.FirstOrDefault(t => t.Id == owner[5..]);
            if (type is not null) HotkeyForType(type);
        }
        else if (owner.StartsWith("timer:", StringComparison.Ordinal) && Guid.TryParse(owner[6..], out var id))
        {
            var timer = Engine.Timers.FirstOrDefault(t => t.Id == id);
            if (timer is not null) HotkeyForTimer(timer);
        }
    });

    /// <summary>Default action is to start a timer. A stopwatch type toggles its live stopwatch between running and paused.</summary>
    private void HotkeyForType(TimerType type)
    {
        if (type.Kind == TimerKind.Stopwatch)
        {
            var live = Engine.Timers.LastOrDefault(t => t.TypeId == type.Id && t.State is TimerState.Running or TimerState.Paused);
            if (live is not null)
            {
                Toggle(live);
                return;
            }
        }
        Engine.Add(type.CreateTimer());
    }

    private void HotkeyForTimer(TimerItem timer)
    {
        if (timer.State == TimerState.Pending) Engine.Start(timer.Id);
        else Toggle(timer);
    }

    private void Toggle(TimerItem timer)
    {
        if (timer.Kind != TimerKind.Stopwatch) return;
        if (timer.State == TimerState.Running) Engine.Pause(timer.Id);
        else if (timer.State == TimerState.Paused) Engine.Resume(timer.Id);
    }

    private T Load<T>(string path, Func<T> create, int version) where T : class =>
        _store.Load(path, create, new Migrator(version, Array.Empty<IMigration>())).Value;
}

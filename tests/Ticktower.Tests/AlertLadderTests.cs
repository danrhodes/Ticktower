using Ticktower.Core.Engine;
using Ticktower.Core.Models;
using Ticktower.Core.Storage;

namespace Ticktower.Tests;

public sealed class AlertLadderTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ticktower-tests-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = T0;
    private readonly List<AlertNotice> _alerts = new();

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed class Sink : IAlertSink
    {
        private readonly List<AlertNotice> _into;
        public Sink(List<AlertNotice> into) => _into = into;
        public void Alert(AlertNotice n) => _into.Add(n);
    }

    private TimerEngine NewEngine(params AlertStep[] steps)
    {
        var engine = new TimerEngine(new Sink(_alerts), () => _now);
        if (steps.Length > 0)
            engine.AlertSource = t => new EffectiveAlert(steps, null, 80, "x");
        return engine;
    }

    private static TimerItem Countdown(int minutes = 1) =>
        new() { Name = "c", Kind = TimerKind.Countdown, Duration = TimeSpan.FromMinutes(minutes) };

    private static AlertStep Step(AlertChannels c, int seconds) => new(c, TimeSpan.FromSeconds(seconds));

    [Fact]
    public void Default_ladder_matches_the_guide()
    {
        var steps = BuiltInAlertProfiles.Create().Single(p => p.Id == "default").Steps.Select(s => s.Channels).ToArray();
        Assert.Equal(new[]
        {
            AlertChannels.Toast,
            AlertChannels.Toast | AlertChannels.Sound,
            AlertChannels.Overlay | AlertChannels.Sound,
            AlertChannels.Overlay | AlertChannels.Tts,
        }, steps);
    }

    [Fact]
    public void Ladder_climbs_one_rung_per_repeat_interval_and_caps_at_the_last()
    {
        var engine = NewEngine(Step(AlertChannels.Toast, 10), Step(AlertChannels.Sound, 20), Step(AlertChannels.Overlay, 30));
        engine.Add(Countdown());

        _now = T0.AddMinutes(1);
        engine.Tick();                                   // rung 0 at expiry
        _now += TimeSpan.FromSeconds(9);
        engine.Tick();                                   // too early
        _now += TimeSpan.FromSeconds(1);
        engine.Tick();                                   // rung 1 after 10s
        _now += TimeSpan.FromSeconds(20);
        engine.Tick();                                   // rung 2 after 20s
        _now += TimeSpan.FromSeconds(30);
        engine.Tick();                                   // last rung repeats
        _now += TimeSpan.FromSeconds(30);
        engine.Tick();

        Assert.Equal(new[] { 0, 1, 2, 2, 2 }, _alerts.Select(a => a.StepIndex));
        Assert.Equal(AlertChannels.Sound, _alerts[1].Step.Channels);
    }

    [Fact]
    public void Acknowledge_stops_the_repeats()
    {
        var engine = NewEngine(Step(AlertChannels.Toast, 10));
        var t = engine.Add(Countdown());
        _now = T0.AddMinutes(1);
        engine.Tick();
        engine.Acknowledge(t.Id);

        _now += TimeSpan.FromMinutes(5);
        engine.Tick();
        Assert.Single(_alerts);
    }

    [Fact]
    public void Snooze_stops_repeats_and_restarts_the_ladder_on_next_expiry()
    {
        var engine = NewEngine(Step(AlertChannels.Toast, 10), Step(AlertChannels.Sound, 10));
        var t = engine.Add(Countdown());
        _now = T0.AddMinutes(1);
        engine.Tick();
        _now += TimeSpan.FromSeconds(10);
        engine.Tick();
        Assert.Equal(new[] { 0, 1 }, _alerts.Select(a => a.StepIndex));

        engine.Snooze(t.Id, TimeSpan.FromMinutes(5));
        _now += TimeSpan.FromMinutes(1);
        engine.Tick();
        Assert.Equal(2, _alerts.Count);

        _now += TimeSpan.FromMinutes(4);
        engine.Tick();
        Assert.Equal(new[] { 0, 1, 0 }, _alerts.Select(a => a.StepIndex));
    }

    [Fact]
    public void Relaunch_continues_the_ladder_from_the_event_log()
    {
        var engine = NewEngine(Step(AlertChannels.Toast, 10), Step(AlertChannels.Sound, 10));
        engine.Add(Countdown());
        _now = T0.AddMinutes(1);
        engine.Tick();

        var snapshot = engine.Snapshot().Timers;
        var relaunched = NewEngine(Step(AlertChannels.Toast, 10), Step(AlertChannels.Sound, 10));
        relaunched.Load(snapshot);
        _alerts.Clear();

        _now += TimeSpan.FromMinutes(30);   // app was closed for half an hour
        relaunched.Recompute();

        var n = Assert.Single(_alerts);
        Assert.Equal(1, n.StepIndex);       // continues on rung 1, not rung 0 again
        Assert.True(n.Missed);
    }

    [Fact]
    public void Zero_repeat_interval_is_floored_so_it_cannot_storm()
    {
        var engine = NewEngine(Step(AlertChannels.Toast, 0));
        engine.Add(Countdown());
        _now = T0.AddMinutes(1);
        engine.Tick();
        engine.Tick();
        engine.Tick();
        Assert.Single(_alerts);
    }

    [Fact]
    public void Resolver_uses_profile_then_layers_the_timer_override()
    {
        var profile = new AlertProfile
        {
            Id = "p", Name = "P", Volume = 40, SoundFile = "a.wav", TtsTemplate = "profile",
            Steps = { Step(AlertChannels.Toast, 5) },
        };
        var t = new TimerItem { AlertProfileId = "p" };

        var plain = AlertResolver.Resolve(t, new[] { profile });
        Assert.Equal(40, plain.Volume);
        Assert.Equal("a.wav", plain.SoundFile);

        t.AlertOverride = new AlertOverride { Volume = 95, Steps = new() { Step(AlertChannels.Overlay, 7) } };
        var over = AlertResolver.Resolve(t, new[] { profile });
        Assert.Equal(95, over.Volume);
        Assert.Equal("a.wav", over.SoundFile);          // not overridden, inherited
        Assert.Equal("profile", over.TtsTemplate);
        Assert.Equal(AlertChannels.Overlay, over.Steps[0].Channels);
    }

    [Fact]
    public void Resolver_falls_back_to_default_for_unknown_profile_and_empty_steps()
    {
        var t = new TimerItem { AlertProfileId = "gone" };
        var r = AlertResolver.Resolve(t, BuiltInAlertProfiles.Create());
        Assert.Equal(4, r.Steps.Count);

        var empty = new AlertProfile { Id = "e", Name = "E" };
        t.AlertProfileId = "e";
        Assert.NotEmpty(AlertResolver.Resolve(t, new[] { empty }).Steps);
    }

    [Fact]
    public void Resolver_clamps_volume()
    {
        var t = new TimerItem { AlertOverride = new AlertOverride { Volume = 500 } };
        Assert.Equal(100, AlertResolver.Resolve(t, BuiltInAlertProfiles.Create()).Volume);
    }

    [Fact]
    public void Tts_template_fills_fields_and_collapses_blanks()
    {
        var t = new TimerItem { Name = "Call back", Ticket = "", Client = "Acme", Priority = Priority.P1 };
        Assert.Equal("Call back Acme has expired", AlertText.Format(AlertProfile.DefaultTtsTemplate, t));
        Assert.Equal("P1 for Acme", AlertText.Format("{priority} for {client}", t));
    }

    [Fact]
    public void Profiles_file_round_trips_flag_channels()
    {
        var path = new AppPaths(_dir).Root + "/alertprofiles.json";
        var store = new JsonFileStore();
        store.Save(path, new AlertProfilesFile());
        var loaded = store.Load(path, () => new AlertProfilesFile());
        Assert.Equal(LoadOutcome.Loaded, loaded.Outcome);
        var def = loaded.Value.Profiles.Single(p => p.Id == "default");
        Assert.Equal(AlertChannels.Overlay | AlertChannels.Tts, def.Steps[3].Channels);
    }

    [Fact]
    public void Timer_override_survives_a_state_round_trip()
    {
        var path = _dir + "/state.json";
        var t = Countdown();
        t.AlertOverride = new AlertOverride { Volume = 10, Steps = new() { Step(AlertChannels.Tts, 3) } };
        var store = new JsonFileStore();
        store.Save(path, new StateFile { Timers = { t } });
        var back = store.Load(path, () => new StateFile()).Value.Timers[0];
        Assert.Equal(10, back.AlertOverride!.Volume);
        Assert.Equal(AlertChannels.Tts, back.AlertOverride.Steps![0].Channels);
    }

    [Fact]
    public void V1_state_files_migrate_to_v2_without_loss()
    {
        var path = _dir + "/state.json";
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{"version":1,"timers":[{"id":"11111111-1111-1111-1111-111111111111","name":"old","kind":"Countdown","state":"Running","events":[]}]}""");
        var result = new JsonFileStore().Load(path, () => new StateFile(), StateMigrations.Create());
        Assert.Equal(LoadOutcome.Migrated, result.Outcome);
        Assert.Equal(2, result.Value.Version);
        Assert.Equal("old", result.Value.Timers[0].Name);
        Assert.Null(result.Value.Timers[0].AlertOverride);
    }
}

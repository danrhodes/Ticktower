using System.Text.Json.Nodes;
using Ticktower.Core.Models;
using Ticktower.Core.Storage;

namespace Ticktower.Tests;

public sealed class JsonFileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ticktower-tests-" + Guid.NewGuid().ToString("N"));

    public JsonFileStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string P(string name) => Path.Combine(_dir, name);

    private static StateFile Sample()
    {
        var t0 = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);
        var timer = new TimerItem
        {
            Name = "P1 update",
            Kind = TimerKind.Interval,
            Priority = Priority.P1,
            Duration = TimeSpan.FromMinutes(15),
            StartedUtc = t0,
            EndUtc = t0.AddMinutes(15),
        };
        timer.Events.Add(new TimerEvent(TimerEventType.Start, t0, "note"));
        return new StateFile { Timers = { timer } };
    }

    [Fact]
    public void Save_then_load_round_trips_including_utc_kind()
    {
        var store = new JsonFileStore();
        var original = Sample();
        store.Save(P("state.json"), original);

        var result = store.Load(P("state.json"), () => new StateFile());
        Assert.Equal(LoadOutcome.Loaded, result.Outcome);
        var t = Assert.Single(result.Value.Timers);
        Assert.Equal(original.Timers[0].Id, t.Id);
        Assert.Equal(TimerKind.Interval, t.Kind);
        Assert.Equal(TimeSpan.FromMinutes(15), t.Duration);
        Assert.Equal(DateTimeKind.Utc, t.StartedUtc!.Value.Kind);
        Assert.Equal(original.Timers[0].EndUtc, t.EndUtc);
        Assert.Equal("note", t.Events[0].Note);
    }

    [Fact]
    public void Saved_file_carries_version_and_string_enums()
    {
        new JsonFileStore().Save(P("state.json"), Sample());
        var text = File.ReadAllText(P("state.json"));
        Assert.Contains("\"version\": 2", text);
        Assert.Contains("\"Interval\"", text);
    }

    [Fact]
    public void Missing_file_returns_defaults_without_backup()
    {
        var result = new JsonFileStore().Load(P("state.json"), () => new StateFile { Version = 1 });
        Assert.Equal(LoadOutcome.Missing, result.Outcome);
        Assert.Empty(result.Value.Timers);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Corrupt_file_is_backed_up_as_bad_and_defaults_returned()
    {
        File.WriteAllText(P("state.json"), "{ not json");
        var result = new JsonFileStore().Load(P("state.json"), () => new StateFile());

        Assert.Equal(LoadOutcome.Recovered, result.Outcome);
        Assert.Equal(P("state.json.bad"), result.BackupPath);
        Assert.Equal("{ not json", File.ReadAllText(P("state.json.bad")));
        Assert.False(File.Exists(P("state.json")));
    }

    [Fact]
    public void Empty_file_counts_as_corrupt()
    {
        File.WriteAllText(P("state.json"), "");
        var result = new JsonFileStore().Load(P("state.json"), () => new StateFile());
        Assert.Equal(LoadOutcome.Recovered, result.Outcome);
        Assert.True(File.Exists(P("state.json.bad")));
    }

    [Fact]
    public void Second_corruption_overwrites_previous_bad_file()
    {
        File.WriteAllText(P("state.json.bad"), "old");
        File.WriteAllText(P("state.json"), "{ nope");
        new JsonFileStore().Load(P("state.json"), () => new StateFile());
        Assert.Equal("{ nope", File.ReadAllText(P("state.json.bad")));
    }

    [Fact]
    public void Save_replaces_existing_file_and_leaves_no_temp()
    {
        var store = new JsonFileStore();
        store.Save(P("state.json"), new StateFile());
        store.Save(P("state.json"), Sample());

        Assert.Equal(new[] { P("state.json") }, Directory.GetFiles(_dir));
        Assert.Single(store.Load(P("state.json"), () => new StateFile()).Value.Timers);
    }

    [Fact]
    public void Save_creates_missing_directories()
    {
        var path = Path.Combine(_dir, "a", "b", "state.json");
        new JsonFileStore().Save(path, new StateFile());
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Failed_save_leaves_original_untouched()
    {
        var store = new JsonFileStore();
        store.Save(P("state.json"), Sample());
        var before = File.ReadAllText(P("state.json"));

        // A directory squatting on the temp path makes the temp write fail before any replace.
        Directory.CreateDirectory(P("state.json.tmp"));
        Assert.ThrowsAny<Exception>(() => store.Save(P("state.json"), new StateFile()));
        Assert.Equal(before, File.ReadAllText(P("state.json")));
    }

    [Fact]
    public void Stale_temp_file_from_a_crash_is_ignored_and_overwritten()
    {
        File.WriteAllText(P("state.json.tmp"), "half written");
        var store = new JsonFileStore();
        store.Save(P("state.json"), Sample());
        Assert.False(File.Exists(P("state.json.tmp")));
        Assert.Single(store.Load(P("state.json"), () => new StateFile()).Value.Timers);
    }

    [Fact]
    public void Load_runs_migrations_and_reports_migrated()
    {
        File.WriteAllText(P("state.json"), """{ "version": 1, "legacyName": "x", "timers": [] }""");
        var migrator = new Migrator(2, new[] { new RenameMigration() });

        var result = new JsonFileStore().Load(P("state.json"), () => new StateFile(), migrator);
        Assert.Equal(LoadOutcome.Migrated, result.Outcome);
        Assert.Equal(2, result.Value.Version);
    }

    [Fact]
    public void File_from_a_newer_version_is_backed_up_not_lost()
    {
        File.WriteAllText(P("state.json"), """{ "version": 99, "timers": [] }""");
        var result = new JsonFileStore().Load(P("state.json"), () => new StateFile(), new Migrator(1, Array.Empty<IMigration>()));
        Assert.Equal(LoadOutcome.Recovered, result.Outcome);
        Assert.True(File.Exists(P("state.json.bad")));
    }

    [Fact]
    public void File_without_version_is_treated_as_corrupt()
    {
        File.WriteAllText(P("state.json"), """{ "timers": [] }""");
        var result = new JsonFileStore().Load(P("state.json"), () => new StateFile(), new Migrator(1, Array.Empty<IMigration>()));
        Assert.Equal(LoadOutcome.Recovered, result.Outcome);
    }

    private sealed class RenameMigration : IMigration
    {
        public int FromVersion => 1;
        public void Apply(JsonObject root) => root.Remove("legacyName");
    }
}

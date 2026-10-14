using Ticktower.Core.Models;
using Ticktower.Core.Storage;

namespace Ticktower.Tests;

public sealed class TimerTypeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ticktower-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Built_ins_include_the_five_intervals_a_countdown_and_a_stopwatch()
    {
        var types = BuiltInTypes.Create();
        var intervals = types.Where(t => t.Kind == TimerKind.Interval).Select(t => (int)t.DefaultDuration.TotalMinutes);
        Assert.Equal(new[] { 5, 10, 15, 30, 60 }, intervals);
        Assert.Contains(types, t => t.Kind == TimerKind.Countdown);
        Assert.Contains(types, t => t.Kind == TimerKind.Stopwatch);
        Assert.All(types, t => Assert.True(t.BuiltIn));
        Assert.Equal(types.Count, types.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void CreateTimer_applies_type_defaults_and_overrides()
    {
        var type = BuiltInTypes.Create().Single(t => t.Id == "interval-15");
        var t = type.CreateTimer(ticket: "INC1", client: "Acme", priority: Priority.P1);
        Assert.Equal("15 min", t.Name);
        Assert.Equal(TimerKind.Interval, t.Kind);
        Assert.Equal(TimeSpan.FromMinutes(15), t.Duration);
        Assert.Equal(Priority.P1, t.Priority);
        Assert.Equal("interval-15", t.TypeId);
        Assert.Equal("Acme", t.Client);

        Assert.Equal("Call back", type.CreateTimer("Call back").Name);
    }

    [Fact]
    public void Types_file_defaults_to_built_ins_and_round_trips()
    {
        var path = new AppPaths(_dir).Types;
        var store = new JsonFileStore();
        var first = store.Load(path, () => new TypesFile());
        Assert.Equal(LoadOutcome.Missing, first.Outcome);
        Assert.Equal(7, first.Value.Types.Count);

        store.Save(path, first.Value);
        var again = store.Load<TypesFile>(path, () => new TypesFile());
        Assert.Equal(LoadOutcome.Loaded, again.Outcome);
        Assert.Equal(7, again.Value.Types.Count);
        Assert.Equal(TimeSpan.FromMinutes(5), again.Value.Types[0].DefaultDuration);
    }
}

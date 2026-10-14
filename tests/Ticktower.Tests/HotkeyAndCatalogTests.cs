using Ticktower.Core;
using Ticktower.Core.Models;

namespace Ticktower.Tests;

public sealed class HotkeyAndCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ticktower-tests-" + Guid.NewGuid().ToString("N"));

    public HotkeyAndCatalogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("ctrl+alt+k", "Ctrl+Alt+K")]
    [InlineData("Shift + Control + F5", "Ctrl+Shift+F5")]
    [InlineData("win+1", "Win+1")]
    [InlineData("Alt+F24", "Alt+F24")]
    public void Parses_and_canonicalises(string text, string expected)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(expected, hk.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("K")]               // no modifier
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]        // no key
    [InlineData("Ctrl+K+L")]        // two keys
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+Space")]
    [InlineData("Ctrl+;")]
    public void Rejects_invalid(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Fact]
    public void Virtual_keys_and_modifier_flags_match_win32()
    {
        Hotkey.TryParse("Ctrl+Alt+A", out var a);
        Hotkey.TryParse("Shift+7", out var seven);
        Hotkey.TryParse("Win+F1", out var f1);
        Hotkey.TryParse("Ctrl+F12", out var f12);
        Assert.Equal(0x41, a.VirtualKey);
        Assert.Equal(0x37, seven.VirtualKey);
        Assert.Equal(0x70, f1.VirtualKey);
        Assert.Equal(0x7B, f12.VirtualKey);
        Assert.Equal(1 | 2, (int)a.Modifiers);
        Assert.Equal(8, (int)f1.Modifiers);
    }

    [Fact]
    public void Book_rejects_duplicates_and_allows_reassigning_the_same_owner()
    {
        var book = new HotkeyBook();
        Hotkey.TryParse("Ctrl+Alt+K", out var k);
        Hotkey.TryParse("ALT+CTRL+k", out var sameKey);
        Hotkey.TryParse("Ctrl+Alt+L", out var l);

        Assert.True(book.TryAssign("type:a", k, out _));
        Assert.False(book.TryAssign("type:b", sameKey, out var conflict));
        Assert.Equal("type:a", conflict);
        Assert.True(book.TryAssign("type:a", sameKey, out _));      // same owner is fine

        Assert.True(book.TryAssign("type:a", l, out _));            // moving frees the old combo
        Assert.True(book.TryAssign("type:b", k, out _));
        Assert.Equal("type:a", book.OwnerOf(l));

        book.Release("type:a");
        Assert.Null(book.OwnerOf(l));
    }

    private static TimerType Type(string id, string name) =>
        new() { Id = id, Name = name, Kind = TimerKind.Countdown, DefaultDuration = TimeSpan.FromMinutes(5) };

    [Fact]
    public void Export_then_read_round_trips_items()
    {
        var path = Path.Combine(_dir, "types-export.json");
        CatalogIO.Export(path, "types", new[] { Type("a", "A"), Type("b", "B") });
        var back = CatalogIO.Read<TimerType>(path, "types");
        Assert.Equal(new[] { "A", "B" }, back.Select(t => t.Name));
        Assert.Equal(TimeSpan.FromMinutes(5), back[0].DefaultDuration);
    }

    [Fact]
    public void Read_rejects_wrong_kind_garbage_and_future_versions_without_touching_the_file()
    {
        var path = Path.Combine(_dir, "x.json");
        CatalogIO.Export(path, "palettes", new[] { new ColourPalette { Id = "p", Name = "P" } });
        Assert.Throws<InvalidDataException>(() => CatalogIO.Read<TimerType>(path, "types"));

        File.WriteAllText(path, "{ nope");
        Assert.Throws<InvalidDataException>(() => CatalogIO.Read<TimerType>(path, "types"));
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".bad"));

        File.WriteAllText(path, """{"version":99,"kind":"types","items":[]}""");
        Assert.Throws<InvalidDataException>(() => CatalogIO.Read<TimerType>(path, "types"));
    }

    [Fact]
    public void Merge_adds_new_replaces_user_items_and_never_overwrites_built_ins()
    {
        var existing = new List<TimerType> { Type("user", "Old") , new() { Id = "core", Name = "Core", BuiltIn = true } };
        var incoming = new List<TimerType>
        {
            Type("user", "New"),
            Type("fresh", "Fresh"),
            new() { Id = "core", Name = "Hijack", BuiltIn = true },
            Type("", "NoId"),
        };

        var result = CatalogIO.Merge(existing, incoming);

        Assert.Equal(new MergeResult(3, 1), result);
        Assert.Equal("New", existing.Single(t => t.Id == "user").Name);
        Assert.Equal("Core", existing.Single(t => t.Id == "core").Name);
        Assert.Contains(existing, t => t.Name == "Hijack" && t.Id != "core");
        Assert.All(existing.Where(t => t.Id != "core"), t => Assert.False(t.BuiltIn));
        Assert.All(existing, t => Assert.False(string.IsNullOrEmpty(t.Id)));
    }

    [Fact]
    public void Preset_round_trips_a_timer_and_rebuilds_it()
    {
        var types = BuiltInTypes.Create();
        var source = types.Single(t => t.Id == "interval-15").CreateTimer("Client X P1", "INC7", "Client X", Priority.P1);
        source.AlertProfileId = "urgent";
        source.AlertOverride = new AlertOverride { Volume = 100 };

        var preset = Preset.FromTimer("Client X P1", source, chainTypeId: "countdown");
        var json = Path.Combine(_dir, "presets.json");
        CatalogIO.Export(json, "presets", new[] { preset });
        var loaded = CatalogIO.Read<Preset>(json, "presets").Single();

        var timer = loaded.CreateTimer(types)!;
        Assert.Equal(TimerKind.Interval, timer.Kind);
        Assert.Equal(TimeSpan.FromMinutes(15), timer.Duration);
        Assert.Equal(Priority.P1, timer.Priority);
        Assert.Equal("urgent", timer.AlertProfileId);
        Assert.Equal(100, timer.AlertOverride!.Volume);
        Assert.Equal("countdown", loaded.ChainTypeId);
        Assert.NotEqual(source.Id, timer.Id);
        Assert.Empty(timer.Events);
    }

    [Fact]
    public void Preset_with_a_deleted_type_builds_nothing_and_duration_override_applies()
    {
        var types = BuiltInTypes.Create();
        Assert.Null(new Preset { TypeId = "gone" }.CreateTimer(types));

        var p = new Preset { TypeId = "countdown", Duration = TimeSpan.FromMinutes(42) };
        Assert.Equal(TimeSpan.FromMinutes(42), p.CreateTimer(types)!.Duration);

        var sw = new Preset { TypeId = "stopwatch", Duration = TimeSpan.FromMinutes(42) };
        Assert.Equal(TimeSpan.Zero, sw.CreateTimer(types)!.Duration);
    }
}

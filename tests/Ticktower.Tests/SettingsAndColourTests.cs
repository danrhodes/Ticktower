using Ticktower.Core;
using Ticktower.Core.Engine;
using Ticktower.Core.Models;

namespace Ticktower.Tests;

public class SettingsAndColourTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly ColourPalette P = new();

    [Theory]
    [InlineData("5", 300)]
    [InlineData("5m", 300)]
    [InlineData("90s", 90)]
    [InlineData("2h", 7200)]
    [InlineData("1h30m", 5400)]
    [InlineData(" 10 M ", 600)]
    public void Snooze_parser_accepts_common_forms(string text, int seconds)
    {
        Assert.True(SnoozeParser.TryParse(text, out var span));
        Assert.Equal(TimeSpan.FromSeconds(seconds), span);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("5x")]
    [InlineData("m5")]
    [InlineData("25h")]
    [InlineData("1.5")]
    public void Snooze_parser_rejects_bad_input(string? text)
    {
        Assert.False(SnoozeParser.TryParse(text, out _));
    }

    [Fact]
    public void Quick_snooze_is_the_first_positive_preset()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), new SettingsFile().QuickSnooze);
        Assert.Equal(TimeSpan.FromMinutes(5), new SettingsFile { SnoozeMinutes = new() }.QuickSnooze);
        Assert.Equal(TimeSpan.FromMinutes(20), new SettingsFile { SnoozeMinutes = new() { 0, 20 } }.QuickSnooze);
    }

    private static TimerItem Running(int minutes)
    {
        var t = new TimerItem { Kind = TimerKind.Countdown, Duration = TimeSpan.FromMinutes(minutes) };
        TimerStateMachine.Start(t, T0);
        return t;
    }

    [Fact]
    public void Urgency_runs_green_to_amber_to_red()
    {
        var t = Running(100);
        Assert.Equal(P.UrgencyGreen, BoardColours.Urgency(t, T0, P));
        Assert.Equal(P.UrgencyGreen, BoardColours.Urgency(t, T0.AddMinutes(50), P));       // 50% left
        Assert.Equal(P.UrgencyAmber, BoardColours.Urgency(t, T0.AddMinutes(75), P));       // 25% left
        Assert.Equal(P.UrgencyRed, BoardColours.Urgency(t, T0.AddMinutes(100), P));        // 0% left

        var mid = BoardColours.Urgency(t, T0.AddMinutes(62.5), P);                          // 37.5%: halfway green to amber
        Assert.Equal(ColourUtil.Lerp(P.UrgencyGreen, P.UrgencyAmber, 0.5), mid);
    }

    [Fact]
    public void Urgency_expired_is_red_acknowledged_is_done_and_stopwatch_neutral()
    {
        var t = Running(5);
        TimerStateMachine.Expire(t, T0.AddMinutes(6));
        Assert.Equal(P.UrgencyRed, BoardColours.Urgency(t, T0.AddMinutes(6), P));
        TimerStateMachine.Acknowledge(t, T0.AddMinutes(7));
        Assert.Equal(P.Done, BoardColours.Urgency(t, T0.AddMinutes(7), P));

        var sw = new TimerItem { Kind = TimerKind.Stopwatch };
        TimerStateMachine.Start(sw, T0);
        Assert.Equal(P.Neutral, BoardColours.Urgency(sw, T0, P));
    }

    [Fact]
    public void Priority_and_type_modes_read_the_palette_and_type()
    {
        var types = BuiltInTypes.Create();
        var t = new TimerItem { Priority = Priority.P2, TypeId = "interval-15" };
        Assert.Equal(P.Priority[1], BoardColours.For(t, ColourMode.Priority, T0, P, types));
        Assert.Equal(types.Single(x => x.Id == "interval-15").Colour, BoardColours.For(t, ColourMode.Type, T0, P, types));
        Assert.Equal(P.Neutral, BoardColours.For(new TimerItem { TypeId = "gone" }, ColourMode.Type, T0, P, types));
    }

    [Fact]
    public void Client_colour_is_stable_case_insensitive_and_blank_is_neutral()
    {
        var types = BuiltInTypes.Create();
        string C(string client) => BoardColours.For(new TimerItem { Client = client }, ColourMode.Client, T0, P, types);

        Assert.Equal(C("Acme"), C("  acme "));
        Assert.Contains(C("Acme"), P.Client);
        Assert.Equal(P.Neutral, C(""));
        // FNV-1a is fixed, so the value must never change between runs or builds.
        Assert.Equal(0x45FD71AFu, BoardColours.StableHash("acme"));
    }

    [Fact]
    public void Hex_helpers_validate_and_blend()
    {
        Assert.True(ColourUtil.TryParseHex("#0a1B2c", out var rgb));
        Assert.Equal((10, 27, 44), (rgb.R, rgb.G, rgb.B));
        Assert.False(ColourUtil.TryParseHex("0a1b2c", out _));
        Assert.False(ColourUtil.TryParseHex("#12345", out _));
        Assert.False(ColourUtil.TryParseHex("#GG0000", out _));
        Assert.Equal("#808080", ColourUtil.Lerp("#000000", "#FFFFFF", 0.5));
        Assert.Equal("#000000", ColourUtil.Lerp("#000000", "#FFFFFF", -3));
        Assert.Equal("nope", ColourUtil.Lerp("nope", "#FFFFFF", 0.5));
    }

    [Fact]
    public void Built_in_palettes_are_valid_hex_and_complete()
    {
        foreach (var p in BuiltInPalettes.Create())
        {
            var all = new[] { p.UrgencyGreen, p.UrgencyAmber, p.UrgencyRed, p.Neutral, p.Done }.Concat(p.Priority).Concat(p.Client);
            Assert.All(all, c => Assert.True(ColourUtil.TryParseHex(c, out _), c));
            Assert.Equal(4, p.Priority.Count);
            Assert.NotEmpty(p.Client);
        }
    }
}

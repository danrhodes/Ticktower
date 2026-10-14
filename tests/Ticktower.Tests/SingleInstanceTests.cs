using Ticktower.Core;

namespace Ticktower.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void Second_instance_is_not_primary_and_signals_the_first()
    {
        var name = "TicktowerTest" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstance(name);
        Assert.True(first.IsPrimary);

        using var woke = new ManualResetEventSlim();
        first.Listen(woke.Set);

        using var second = new SingleInstance(name);
        Assert.False(second.IsPrimary);
        second.SignalExisting();

        Assert.True(woke.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Name_is_free_again_after_the_primary_disposes()
    {
        var name = "TicktowerTest" + Guid.NewGuid().ToString("N");
        new SingleInstance(name).Dispose();
        using var next = new SingleInstance(name);
        Assert.True(next.IsPrimary);
    }
}

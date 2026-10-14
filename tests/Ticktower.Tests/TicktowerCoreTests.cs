namespace Ticktower.Tests;

using Ticktower.Core;

public class TicktowerCoreTests
{
    [Fact]
    public void TicktowerCore_ExposesProjectName()
    {
        Assert.Equal("Ticktower", TicktowerCore.Name);
    }
}

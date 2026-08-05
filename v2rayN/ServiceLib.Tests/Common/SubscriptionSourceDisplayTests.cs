using Xunit;

namespace ServiceLib.Tests.Common;

public sealed class SubscriptionSourceDisplayTests
{
    [Fact]
    public void Format_ShowsOnlyTheSubscriptionName()
    {
        var display = SubscriptionSourceDisplay.Format("火箭云");

        Assert.Equal("订阅：火箭云", display);
    }

    [Fact]
    public void Format_TrimsTheSubscriptionName()
    {
        Assert.Equal("订阅：机场 A", SubscriptionSourceDisplay.Format("  机场 A  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_UsesPlaceholderWhenNameIsEmpty(string? remarks)
    {
        Assert.Equal("订阅：未命名订阅", SubscriptionSourceDisplay.Format(remarks));
    }
}
